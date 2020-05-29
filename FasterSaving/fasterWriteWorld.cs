using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Assets.Scripts.Serialization;
using Assets.Scripts.Voxel;
using ICanHazCode.ModUtils;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Utils;
using SR = System.Reflection;
using SRE = System.Reflection.Emit;

namespace stationeers.fastersaving
{
	[HarmonyPatch(typeof(XmlSaveLoad), "WriteWorld")]
	class FasterWriteWorld
	{
		#region Logging support
		private static void Log(string s)
		{
			PatchWriteWorld.Instance.Log(s);
		}

		private static void LogError(string error)
		{
			PatchWriteWorld.Instance.LogError(error);
		}
		#endregion
		#region Debugging
		private static string printLocals(Mono.Cecil.Cil.MethodBody methodBody)
		{
			var locals = methodBody.Variables;
			var initLocals = methodBody.InitLocals;
			StringBuilder sb = new StringBuilder($".locals(\n\tMaxStackSize:{methodBody.MaxStackSize}\n\tInit: {initLocals}\n");
			foreach (VariableDefinition local in locals)
			{
				sb.AppendLine(string.Format("\t{0,-3}:\t{1}", local.Index, local.VariableType));
			}
			sb.AppendLine(")\n======================");
			return sb.ToString();

		}
		private static string printLocals(MethodBase methodInfo)
		{
			var locals = methodInfo.GetMethodBody().LocalVariables;
			var localInit = methodInfo.GetMethodBody().InitLocals;

			StringBuilder sb = new StringBuilder($"MethodInfo.locals(\n\tMaxStackSize:{methodInfo.GetMethodBody().MaxStackSize}\n\tInitLocals: {localInit}\n");
			foreach (LocalVariableInfo local in locals)
			{
				sb.AppendLine(string.Format("\t{0,-3}:\t{1}", local.LocalIndex, local.LocalType.FullDescription()));
			}
			sb.AppendLine(")\n======================");
			return sb.ToString();

		}

		private static void printCode(IEnumerable<CodeInstruction> codes,
										ILGenerator gen,
										MethodBase methodInfo,
										string header)
		{

			StringBuilder sb = new StringBuilder(header);
			sb.Append("\nCode:\n");
			sb.Append(printLocals(methodInfo));
			sb.Append(printLocals(gen.GetCecilGen().IL.Body));
			int i = 0;

			foreach (CodeInstruction code in codes)
			{
				string ln = code.labels.Count > 0 ? string.Format("lbl[{0}]", code.labels[0].GetHashCode())
												  : i.ToString();
				i++;
				sb.AppendLine(string.Format("{0,-8}:{1,-10}\t{2}",
											ln,
											code.opcode,
											code.operand is SRE.Label ? $"lbl[{code.operand.GetHashCode()}]" :
											code.operand is LocalBuilder lb ? $"Local:{lb.LocalType} ({lb.LocalIndex})" :
											code.operand is string ? $"\"{code.operand}\"" :
											code.operand is MethodBase info ? info.FullDescription() :
											code.operand
											)
							);
			}

			Log(sb.ToString());

		}
		#endregion

		/// <summary>
		/// Our replacement loop
		/// </summary>
		/// <param name="list">The Asteroid collection</param>
		/// <param name="writer">The stream used by the Asteroid.SerializeBytes() method.</param>
		public static void Serialize_Asteroids(ref HashSet<Asteroid> list, ref BinaryWriter writer)
		{
			if (PatchWriteWorld.Debug)
			{
				Log($"In Serialize_Asteroids:\nHashSet:{list.GetType().FullDescription()}\n\tCount:{list.Count}");
				Log($"BinaryWriter:{writer.GetType().FullDescription()}");
			}
			foreach (Asteroid a in list)
			{
				a.SerializeBytes(ref writer);
			}
		}
		#region Transpilers

		/// <summary>
		/// This Transpiler is used to output code changes when debugging
		/// </summary>
		/// <param name="instructions">methodInfo's original opcodes</param>
		/// <param name="generator">The ILGenerator associated with the instructions</param>
		/// <param name="methodInfo">The original method.</param>
		/// <returns></returns>
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
														SRE.ILGenerator generator,
														SR.MethodBase methodInfo)
		{
			if (PatchWriteWorld.Debug) printCode(instructions, generator, methodInfo, "Before Transpiler:");

			IEnumerable<CodeInstruction> codes;
			bool fail;
			try
			{
				//Out actual transpiler
				codes = Xpiler(instructions, generator).ToArray();
				fail = false;
			}
			catch (Exception ex)
			{
				fail = true;
				LogError($"Error in Xpiler: {ex}");
				codes = null;
			}
			if (!fail)
				if (PatchWriteWorld.Debug) printCode(codes, generator, methodInfo, "After Transpiler:");
			return fail ? instructions : codes;
		}

		//Actual transpiler that does the work
		private static IEnumerable<CodeInstruction> Xpiler(IEnumerable<CodeInstruction> instructions,
														ILGenerator generator)

		{
			List<CodeInstruction> codes = instructions.ToList();
			bool isAborted = false;
			//LocalBuilder hashsetVar = generator.DeclareLocal(typeof(HashSet<Asteroid>));

			//Replace the List<Asteroid> with a HashSet<Asteroid>
			if (ReplaceLocalObject(ref generator, out LocalBuilder hashsetVar))
			{
				// Replace the new List<Asteroid>() with new HashSet<Asteroid>()
				if (ReplaceConstructor(ref codes, ref hashsetVar))
				{
					//Remove the 2 locations where there is a !List<Asteroid>.Contains(asteroid)
					if (RemoveContainsChecks(ref codes, ref hashsetVar))
					{
						//Replace List<>.Add with the HasSet<>.Add and remove the bool return val
						if (ReplaceAdd(ref codes, ref hashsetVar))
						{
							// Replace the for() loop with a call to Serialize_Asteroids from this class
							if (!ReplaceLoop(ref codes, ref hashsetVar))
							{
								LogError("Could not replace Serialize loop.");
								isAborted = true;
							}
						}
						else
						{
							LogError("Could not replace Add calls.");
							isAborted = true;
						}
					}
					else
					{
						LogError("Could not remove Contains checks.");
						isAborted = true;
					}
				}
				else
				{
					LogError("Could not replace constructor!");
					isAborted = true;
				}
			}
			else
			{
				LogError("Could not find List<Asteroid> local variable.");
				isAborted = true;
			}
			if (!isAborted && PatchWriteWorld.Debug)
				Log($"Can find local?:{generator.GetGenDictionary().TryGetValue(hashsetVar, out _)}");
			return isAborted ? instructions : codes;
		}
		#endregion

		#region Transpiler functions

		//TODO: Can probably generalize this function
		private static bool ReplaceLocalObject(ref ILGenerator gen, out LocalBuilder hashsetvar)
		{
			Collection<VariableDefinition> variables = gen.GetGenVariables();

			LocalBuilder hashsetVar = null;
			Type listType = typeof(List<Asteroid>);

			bool found = false;
			if (PatchWriteWorld.Debug) Log($"Variables count: {variables.Count}");
			foreach (VariableDefinition local in variables)
			{
				if (PatchWriteWorld.Debug) Log($"Local: {local.VariableType.FullName}");
				if (local.VariableType.ResolveReflection() == listType)
				{
					if (PatchWriteWorld.Debug) Log($"Found match! [{local.Index}]  {local.VariableType}");
					found = true;
					hashsetVar = gen.ReplaceLocal(local, typeof(HashSet<Asteroid>));
					break;
				}
			}
			if (!found)
			{
				if (PatchWriteWorld.Debug) Log("No match found !?");
			}
			hashsetvar = hashsetVar;
			return hashsetVar != null;

		}

		private static bool ReplaceConstructor(ref List<CodeInstruction> codes, ref LocalBuilder hashsetVar)
		{
			bool retval = false;
			// constructor info to compare
			ConstructorInfo constructorInfo = typeof(List<Asteroid>).GetConstructor(new Type[] { });
			for (int index = 0; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				if (index < 125) //Approximate end of where it should be found
				{
					if (ins.opcode == SRE.OpCodes.Newobj)
					{
						if (PatchWriteWorld.Debug) Log($"Found NEWOBJ on line:[{index}]:{ins.operand.GetType()} Name:{((MethodBase)ins.operand).FullDescription()}");
						if (constructorInfo.Equals(ins.operand))
						{
							if (PatchWriteWorld.Debug) Log("Found constructor");
							// Replace constructor with the HashSet constructor
							codes[index++].operand = typeof(HashSet<Asteroid>).GetConstructor(new Type[] { });
							// Replace local pointer with the hashSet pointer
							codes[index].operand = hashsetVar;
							retval = true;
							break;
						}
					}
				}
				else break; // We hit here means we never the new List<Asteroid>()

			}
			return retval;
		}

		private static bool RemoveContainsChecks(ref List<CodeInstruction> codes, ref LocalBuilder hashsetVar)
		{
			int countedremove = 0;
			Type listtype = typeof(List<Asteroid>);
			MethodInfo listContains = listtype.GetMethod(nameof(List<Asteroid>.Contains), new Type[] { typeof(Asteroid) });
			for (int index = 125; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				//Remove the Contains check .. 2 locations
				if (index < 403) //Approx end of where they should be found
				{
					// This should apply 2x

					// Push List<Asteroid> var to stack
					if (ins.opcode == SRE.OpCodes.Ldloc_S && ((LocalBuilder)ins.operand).LocalType == listtype)
					{
						// Call List<Asteroid>.Contains(value) ... nop whole thing
						if (codes[index + 2].opcode == SRE.OpCodes.Callvirt)
						{
							if (listContains.Equals(codes[index + 2].operand))
							{
								//NOP next 5
								countedremove++;
								if (PatchWriteWorld.Debug) Log($"Found List<Asteroid>.Contains() {countedremove} of 2");
								//Remove unneeded codes
								for (int c = 0; c < 4; c++)
									codes.RemoveAt(index);
								//Fix the compound if()
								codes[index].opcode = SRE.OpCodes.Ldc_I4_1;
								codes[index].operand = null;
								continue;
							}

						}
					}

				}
				else break; //End of search ... should be 2 removals

			}
			return (countedremove == 2);
		}

		private static bool ReplaceAdd(ref List<CodeInstruction> codes, ref LocalBuilder hashsetVar)
		{

			int countedreplace = 0;
			Type listtype = typeof(List<Asteroid>);
			MethodInfo listAdd = listtype.GetMethod(nameof(List<Asteroid>.Add), new Type[] { typeof(Asteroid) });
			//Approx start of search
			for (int index = 110; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				//Replace List<Asteroid>.Add .. 2 locations
				if (index < 403)
				{
					// This should apply 2x

					// Push List<Asteroid> var to stack
					// Push asteroid local to stack
					if (ins.opcode == SRE.OpCodes.Ldloc_S && ((LocalBuilder)ins.operand).LocalType == listtype)
					{
						//Callvirt List<Asteroid>.Add() ?
						if (codes[index + 2].opcode == SRE.OpCodes.Callvirt
							&& listAdd.Equals(codes[index + 2].operand))
						{
							countedreplace++;
							if (PatchWriteWorld.Debug) Log($"Found Add {countedreplace} of 2");
							//Replace the push of List<Asteroid> with HashSet<Asteroid>
							//Skip push local asteroid
							//Replace the callVirt operand

							// ldloc.s HashSet<Asteroid>
							codes[index++].operand = hashsetVar;
							//skip the ldloc.s asteroid
							index++;
							//callvirt HashSet<Asteroid>.Add
							codes[index++].operand = typeof(HashSet<Asteroid>).GetMethod("Add");
							// remove the bool return
							codes.Insert(index, new CodeInstruction(SRE.OpCodes.Pop));
							continue;
						}
					}
				}
				else break;
			}
			return (countedreplace == 2);
		}

		private static bool ReplaceLoop(ref List<CodeInstruction> codes, ref LocalBuilder hashsetVar)
		{
			bool retval = false;
			LocalBuilder localWriter = null;
			//Find the local BinaryWriter
			for (int index = 400; index < 422; index++)
			{
				if (codes[index].opcode == SRE.OpCodes.Ldloc_S
					&& codes[index].operand is LocalBuilder lb
					&& lb.LocalType.Equals(typeof(BinaryWriter)))
				{
					if (PatchWriteWorld.Debug) Log("Found BinaryWriter");
					localWriter = lb;
					break;
				}
			}

			for (int index = 400; index < codes.Count; index++)
			{
				// Now to fix the loop
				//This one will be easy, as we just call a subroutine and cut the loop here
				if (index < 455) //Search should end around ldfld ChunkSize
				{
					//We begin at a nop
					if (codes[index].opcode == SRE.OpCodes.Nop)
					{
						//We start here if we're in the right place
						if (codes[index + 1].opcode == SRE.OpCodes.Ldc_I4_0)
						{
							if (PatchWriteWorld.Debug) Log("Starting loop replacement.");
							//We are .. so:
							// continue nop
							index++;
							// First parameter
							codes[index].opcode = SRE.OpCodes.Ldloca_S;
							codes[index++].operand = hashsetVar;
							//Second Parameter
							codes[index].opcode = SRE.OpCodes.Ldloca_S;
							codes[index++].operand = localWriter;
							// Now call the Function
							MethodInfo fn = typeof(FasterWriteWorld).GetMethod(nameof(Serialize_Asteroids));
							if (PatchWriteWorld.Debug) Log($"Call {fn.FullDescription()}");
							codes[index].opcode = SRE.OpCodes.Call;
							codes[index++].operand = fn;
							//for debugging, or whatever
							codes[index].opcode = SRE.OpCodes.Nop;
							codes[index++].operand = null;
							bool eol = false;
							//Now we remove everything till the load of MemoryStream
							while (!eol)
							{
								//last nop
								if (codes[index].opcode == SRE.OpCodes.Brtrue_S)
								{
									//Sanity check. Make sure next op is load local var MemoryStream
									if (codes[index + 1].opcode == SRE.OpCodes.Ldloc_S
										&& codes[index + 1].operand is LocalBuilder lb
										&& lb.LocalType.Equals(typeof(MemoryStream))
										)
									{
										eol = true;
									}
								}
								//Remove the unneeded codes
								codes.RemoveAt(index);
								//codes[index].opcode = SRE.OpCodes.Nop;
								//codes[index++].operand = null;
							}
							if (PatchWriteWorld.Debug) Log("Finished loop replacement");
							retval = true;
							break;
						}
					}
				}
				else break; //We shouldn't hit here
			}
			return retval;
		}
		#endregion
	}
}
