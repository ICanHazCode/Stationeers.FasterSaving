using Assets.Scripts.Serialization;
using Assets.Scripts.Voxel;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using SR = System.Reflection;
using SRE = System.Reflection.Emit;

namespace stationeers.fastersaving
{
	[HarmonyPatch(typeof(XmlSaveLoad), "WriteWorld")]
	class FasterWriteWorld
	{

		private static void Log(string s)
		{
			PatchWriteWorld.Instance.Log(s);
		}

		private static void LogError(string error)
		{
			PatchWriteWorld.Instance.LogError(error);
		}

		private static void printCode(IEnumerable<CodeInstruction> codes, Collection<VariableDefinition> locals, string header)
		{
			StringBuilder sb = new StringBuilder(header);
			sb.Append("\nCode:\n");
			//cecilGen.DefineLabel();

			sb.AppendLine(".locals(");
			foreach (VariableDefinition local in locals)
			{
				sb.AppendLine(string.Format("\t{0,-3}:\t{1}", local.Index, local.VariableType));
			}
			sb.AppendLine(")\n======================");
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


		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
														SRE.ILGenerator generator,
														SR.MethodBase methodInfo)
		{
			CecilILGenerator cecilGen = generator.GetProxiedShim<CecilILGenerator>();
			Collection<VariableDefinition> locals = cecilGen.IL.Body.Variables;
			if (PatchWriteWorld.Debug) printCode(instructions, locals, "Before Transpiler:");
			//Wrapper to output code changes
			IEnumerable<CodeInstruction> codes;
			bool fail;
			try
			{
				codes = Xpiler(instructions, generator, methodInfo).ToArray();
				fail = false;
			}
			catch (Exception ex)
			{
				fail = true;
				LogError($"Error in Xpiler: {ex}");
				codes = null;
			}
			//locals = cecilGen.IL.Body.Variables;
			if (!fail) if (PatchWriteWorld.Debug) printCode(codes, locals, "After Transpiler:");
			return fail ? instructions : codes;
		}

		public static void Serialize_Asteroids(ref HashSet<Asteroid> list, ref BinaryWriter writer)
		{
			foreach (Asteroid a in list)
			{
				a.SerializeBytes(ref writer);
			}
		}

		private static bool ReplaceConstructor(ref List<CodeInstruction> codes, LocalBuilder hashsetVar)
		{
			bool retval = false;
			//Log($"Constructor by typeof     :{typeof(List<Asteroid>).GetConstructor(new Type[] { }).FullDescription()}");
			ConstructorInfo constructorInfo = typeof(List<Asteroid>).GetConstructor(new Type[] { });
			//var constructorInfo = typeof(HashSet<Asteroid>).GetMethod(".ctor",)
			for (int index = 0; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				if (index < 123)
				{
					if (ins.opcode == SRE.OpCodes.Newobj)
					{
						if (PatchWriteWorld.Debug)
						{
							Log($"Found newobj opcode line:{index}");
							Log($"Operand:{ins.operand.GetType()}");
							Log($"Operand:{((MethodBase)ins.operand).FullDescription()}");
						}
						if (constructorInfo.Equals(ins.operand))
						{

							//CecilGen.IL.Create();
							if (PatchWriteWorld.Debug) Log("Found constructor");
							ConstructorInfo hsC = typeof(HashSet<Asteroid>).GetConstructor(new Type[] { });
							codes[index++] = new CodeInstruction(SRE.OpCodes.Newobj, hsC);
							codes[index++] = new CodeInstruction(SRE.OpCodes.Stloc_S, hashsetVar);
							retval = true;
							break;
						}
					}
				}

			}
			return retval;
		}

		private static bool RemoveContainsChecks(ref List<CodeInstruction> codes, LocalBuilder hashsetVar)
		{
			bool retval = false;
			int countedremove = 0;
			Type listtype = typeof(List<Asteroid>);
			for (int index = 123; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				//NOP the Contains check .. 2 locations
				if (index < 403)
				{
					// Push List<Asteroid> var to stack
					// This should apply 2x
					if (ins.opcode == SRE.OpCodes.Ldloc_S && ((LocalBuilder)ins.operand).LocalType == listtype)
					{
						// Call List<Asteroid>.Contains(value) ... nop whole thing
						if (codes[index + 2].opcode == SRE.OpCodes.Callvirt)
						{
							if (typeof(List<Asteroid>)
									.GetMethod(nameof(List<Asteroid>.Contains), new Type[] { typeof(Asteroid) })
									.Equals(codes[index + 2].operand))
							{
								//NOP next 5 
								countedremove++;
								if (PatchWriteWorld.Debug) Log($"Found contains {countedremove} of 2");
								for (int t = index + 5; index < t; index++)
								{
									codes[index] = new CodeInstruction(SRE.OpCodes.Nop);
								}
								continue;
							}

						}
					}

				}
				if (index > 403) break;

			}
			if (countedremove == 2)
			{
				retval = true;
			}
			return retval;
		}

		private static bool ReplaceAdd(ref List<CodeInstruction> codes, LocalBuilder hashsetVar)
		{

			bool retval = false;
			int countedreplace = 0;
			Type listtype = typeof(List<Asteroid>);
			for (int index = 123; index < codes.Count; index++)
			{
				CodeInstruction ins = codes[index];
				//NOP the Contains check .. 2 locations
				if (index < 403)
				{
					// Push List<Asteroid> var to stack
					// This should apply 2x
					if (ins.opcode == SRE.OpCodes.Ldloc_S && ((LocalBuilder)ins.operand).LocalType == listtype)
					{
						if (codes[index + 2].opcode == SRE.OpCodes.Callvirt)
						{
							//Call List<Asteroid>.Add(value) change to HashSet<Asteroid>.Add(value)
							if (typeof(List<Asteroid>)
									.GetMethod(nameof(List<Asteroid>.Add), new Type[] { typeof(Asteroid) })
									.Equals(codes[index + 2].operand))
							{
								countedreplace++;
								if (PatchWriteWorld.Debug) Log($"Found Add {countedreplace} of 2");
								codes[index++] = new CodeInstruction(SRE.OpCodes.Ldloc_S, hashsetVar);
								index++;
								codes[index] = new CodeInstruction(SRE.OpCodes.Callvirt, AccessTools.Method(typeof(HashSet<Asteroid>), nameof(HashSet<Asteroid>.Add)));
								continue;

							}
						}
					}
				}
				if (index > 403) break;
			}
			if (countedreplace == 2) retval = true;
			return retval;
		}

		private static bool ReplaceLoop(ref List<CodeInstruction> codes, LocalBuilder hashsetVar)
		{
			bool retval = false;
			LocalBuilder localWriter = null;
			//Find the local BinaryWriter
			for (int index = 423; index < 454; index++)
			{
				if (codes[index].opcode == SRE.OpCodes.Ldloc_S)
				{
					if (codes[index].operand is LocalBuilder lb
						&& lb.LocalType.Equals(typeof(System.IO.BinaryWriter)))
					{
						if (PatchWriteWorld.Debug) Log("Found BinaryWriter");
						localWriter = lb;
						break;
					}
				}
			}

			for (int index = 423; index < codes.Count; index++)
			{
				// Now to fix the loop
				//This one will be easy, as we just call a subroutine and nop the loop here
				if (index < 455) // Should be around ldfld ChunkSize
				{
					//We begin at a nop
					if (codes[index].opcode == SRE.OpCodes.Nop)
					{
						//We start here if we're in the right place
						if (codes[index + 1].opcode == SRE.OpCodes.Ldc_I4_0)
						{
							//We are .. so:
							// continue nop
							if (PatchWriteWorld.Debug) Log("Starting loop replacement.");
							index++;
							// First parameter
							codes[index++] = new CodeInstruction(SRE.OpCodes.Ldloca_S, hashsetVar);
							//Second Parameter
							codes[index++] = new CodeInstruction(SRE.OpCodes.Ldloca_S, localWriter);
							// Now call the Function
							MethodInfo fn = typeof(FasterWriteWorld).GetMethod(nameof(Serialize_Asteroids));
							if (PatchWriteWorld.Debug) Log($"fn {fn.FullDescription()}");
							codes[index++] = new CodeInstruction(SRE.OpCodes.Call, fn);
							bool eol = false;
							//Now we nop everything till the load of MemoryStream
							while (!eol)
							{
								//last nop
								if (codes[index].opcode == SRE.OpCodes.Brtrue_S)
								{
									//Sanity check
									if (codes[index + 1].opcode == SRE.OpCodes.Ldloc_S
										&& codes[index + 1].operand is LocalBuilder lb
										&& lb.LocalType.Equals(typeof(MemoryStream))
										)
									{
										eol = true;
									}
								}
								codes[index++] = new CodeInstruction(SRE.OpCodes.Nop);
							}
							if (PatchWriteWorld.Debug) Log("Finished loop replacement");
							retval = true;
							break;
						}
					}
				}
			}
			return retval;
		}

		private static bool ReplaceLocalObject(ref ILGenerator gen, out LocalBuilder hashsetvar)
		{
			CecilILGenerator CecilGen = gen.GetProxiedShim<CecilILGenerator>();
			Mono.Cecil.Cil.MethodBody body = CecilGen.IL.Body;
			MethodDefinition method = body.Method;
			ModuleDefinition module = method.Module;
			Dictionary<LocalBuilder, VariableDefinition> locals = (Dictionary<LocalBuilder, VariableDefinition>)AccessTools
																	.Field(typeof(CecilILGenerator), "_Variables")
																	.GetValue(CecilGen);

			VariableDefinition hashSetDef = new VariableDefinition(module.ImportReference(typeof(HashSet<Asteroid>)));

			LocalBuilder hashsetVar = null;
			{

				TypeReference listTypeRef = module.ImportReference(typeof(List<Asteroid>));
				if (PatchWriteWorld.Debug) Log($"listTypeRef:{listTypeRef.FullName}");

				VariableDefinition listVarDef = null;
				foreach (VariableDefinition local in body.Variables)
				{
					if (PatchWriteWorld.Debug) Log($"Local:{local.VariableType.FullName}");
					if (local.VariableType.ResolveReflection() == listTypeRef.ResolveReflection())
					{
						listVarDef = local;
						KeyValuePair<LocalBuilder, VariableDefinition> listLB = locals.First((x) => x.Value.Index == listVarDef.Index);
						if (PatchWriteWorld.Debug)
						{
							Log($"Found match! {local.Index}");
							Log($"listVarDef:{listVarDef.VariableType}");
							Log($"Found Local Index:{listLB.Key.LocalIndex}");
							Log($"Variables count:{body.Variables.Count}");
						}
						hashsetVar = CecilGen.ReplaceLocal(listLB, typeof(HashSet<Asteroid>));
						break;
					}
				}
				if (listVarDef == null)
				{
					if (PatchWriteWorld.Debug) Log("No match found !!!?");
				}
			}
			hashsetvar = hashsetVar;
			return hashsetVar != null;

		}
		private static IEnumerable<CodeInstruction> Xpiler(IEnumerable<CodeInstruction> instructions,
														ILGenerator generator,
														SR.MethodBase methodInfo)

		{
			List<CodeInstruction> codes = instructions.ToList();
			bool isAborted = false;
			if (ReplaceLocalObject(ref generator, out LocalBuilder hashsetVar))
			{
				if (ReplaceConstructor(ref codes, hashsetVar))
				{
					if (RemoveContainsChecks(ref codes, hashsetVar))
					{
						if (ReplaceAdd(ref codes, hashsetVar))
						{
							if (!ReplaceLoop(ref codes, hashsetVar))
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
			if (isAborted) return instructions;
			return codes;
		}
	}
}
