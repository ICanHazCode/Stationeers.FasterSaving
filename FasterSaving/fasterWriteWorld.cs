using Assets.Scripts.Serialization;
using Assets.Scripts.Voxel;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace net.icanhazcode.stationeers.fastersaving
{
	[HarmonyPatch(typeof(XmlSaveLoad), "WriteWorld")]
	public static class FasterWriteWorld
	{
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			// to do the enumeration
			LocalBuilder hashset_enumerator = generator.DeclareLocal(typeof(HashSet<Asteroid>.Enumerator));
			int hashsetEnumerator = hashset_enumerator.LocalIndex;

			//location of new List<Asteroid>()
			bool FoundListInit = false;
			//List<Asteroid> local var list index
			ushort FoundListInitStack = 0;
			//current progress through code list
			int tracking = 0;
			//
			int nullify_Start;
			int nullify_end = 0;

			List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
			for (int i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Newobj)
				{
					if (typeof(List<Asteroid>).GetConstructor(Type.EmptyTypes).Equals(codes[i].operand))
					{
						if (codes[i + 1].opcode == OpCodes.Stloc_S)
						{
							FoundListInit = true;
							FoundListInitStack = (ushort)codes[i + 1].operand;
							codes[i].operand = typeof(HashSet<Asteroid>).GetConstructor(Type.EmptyTypes);
							tracking = i + 1;
							break;
						}

					}
				}
			}
			if (!FoundListInit)
			{

			}

			{
				int tempAsteroidID = 0;
				//if (asteroid != null && !list.Contains(asteroid))
				for (int i = tracking; i < codes.Count; i++)
				{
					if (codes[i].opcode == OpCodes.Call
						&& codes[i + 1].opcode == OpCodes.Isinst
						&& codes[i + 1].operand is Asteroid)
					{
						i += 2;

						tempAsteroidID = (ushort)codes[i].operand;
						tracking = i;
						break;
					}
				}
				for (int i = tracking; i < codes.Count; i++)
				{
					if (codes[1].opcode == OpCodes.Ldnull && codes[i + 1].opcode == OpCodes.Call)
					{
						i += 2;
						nullify_Start = i;
						for (int ending = i; ending < codes.Count; ending++)
						{
							if (codes[ending].opcode == OpCodes.Ldloc_S && (ushort)codes[ending].operand == tempAsteroidID + 1)
							{
								nullify_end = ending;
								tracking = ending;
								break;
							}
						}
						if (nullify_end > 0)
						{
							codes.RemoveRange(nullify_Start, nullify_end - nullify_Start);
							tracking = nullify_Start;
							break;
						}
					}
				}
				//change List<>.Add(asteroid) to HashSet<>.Add(asteroid)
				for (int i = tracking; i < codes.Count; i++)
				{
					if (codes[i].opcode == OpCodes.Ldloc_S && (ushort)codes[i].operand == FoundListInitStack)
					{
						if (codes[++i].opcode == OpCodes.Ldloc_S && (ushort)codes[i].operand == tempAsteroidID)
						{
							if (codes[++i].opcode == OpCodes.Callvirt)
							{
								codes[i].operand = typeof(HashSet<Asteroid>).GetMethod("Add", new Type[] { typeof(Asteroid) });
								tracking = i + 1;
								break;
							}

						}
					}
				}
			}
			// remove the !List<Asteroid>.contains()
			for (int i = tracking; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Callvirt && typeof(List<Asteroid>).GetMethod("Contains", new Type[] { typeof(Asteroid) }).Equals(codes[i].operand))
				{
					i -= 3;
					codes.RemoveRange(i, 6);
					tracking = i; //should be br.s {+2 codes}
					break;
				}
			}
			//Change List<Asteroid>.Add() to HashSet<Asteroid>.Add()
			for (int i = tracking; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Callvirt
					&& typeof(List<Asteroid>).GetMethod("Add",
												new Type[] { typeof(Asteroid) })
												.Equals(codes[i].operand))
				{
					codes[i].operand = typeof(HashSet<Asteroid>).GetMethod("Add", new Type[] { typeof(Asteroid) });
					tracking = i + 1; // Should be pointing to a nop
					break;
				}
			}
			ushort binaryWriterIndex = 0;
			// get the binaryWriter local index
			for (int i = tracking; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Callvirt
					&& typeof(System.IO.BinaryWriter)
							.GetMethod("Write", new Type[] { typeof(int) })
							.Equals(codes[i].operand))
				{
					binaryWriterIndex = (ushort)codes[i - 2].operand;
					tracking = i + 6;
					break;
				}
			}
			//change the for loop to a while enumerable
			int loopstart = tracking;
			int loopend = 0;
			for (int i = tracking; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Callvirt && typeof(System.IO.MemoryStream).GetMethod("GetBuffer").Equals(codes[i]))
				{
					loopend = i - 2;
					break;
				}
			}
			tracking++;
			codes[tracking].opcode = OpCodes.Ldloc_S;
			codes[tracking++].operand = (byte)FoundListInitStack;
			codes[tracking].opcode = OpCodes.Callvirt;
			codes[tracking++].operand = typeof(HashSet<Asteroid>).GetMethod("GetEnumerator");
			codes[tracking].opcode = OpCodes.Stloc_S;
			codes[tracking++].operand = (byte)hashsetEnumerator;
			codes[tracking].opcode = OpCodes.Br_S;
			int futureBR = tracking;
			codes[++tracking].opcode = OpCodes.Ldloca_S;
			int myLoop = tracking;
			codes[tracking++].operand = (byte)hashsetEnumerator;
			codes[tracking].opcode = OpCodes.Call;
			codes[tracking++].operand = typeof(HashSet<Asteroid>.Enumerator).GetMethod("get_Current");
			codes[tracking].opcode = OpCodes.Ldloca_S;
			codes[tracking++].operand = binaryWriterIndex;
			codes[tracking].opcode = OpCodes.Callvirt;
			codes[tracking++].operand = typeof(Asteroid).GetMethod("SerializeBytes", new Type[] { typeof(System.IO.BinaryWriter) });
			codes[tracking].opcode = OpCodes.Ldloca_S;
			codes[tracking++].operand = (byte)hashsetEnumerator;
			codes[tracking].opcode = OpCodes.Call;
			codes[futureBR].operand = (sbyte)(tracking - futureBR);
			codes[tracking++].operand = typeof(HashSet<Asteroid>.Enumerator).GetMethod("MoveNext");
			codes[tracking].opcode = OpCodes.Brtrue_S;
			codes[tracking].operand = (sbyte)(myLoop - tracking);
			codes[++tracking].opcode = OpCodes.Leave_S;
			int ldloc_s_findMemoryStream = tracking;
			codes[++tracking].opcode = OpCodes.Ldloca_S;
			codes[tracking++].operand = (byte)hashsetEnumerator;
			codes[tracking].opcode = OpCodes.Constrained;
			codes[tracking++].operand = typeof(HashSet<Asteroid>.Enumerator);
			codes[tracking].opcode = OpCodes.Callvirt;
			codes[tracking++].operand = typeof(System.IDisposable).GetMethod("Dispose");
			codes[tracking].opcode = OpCodes.Endfinally;
			codes[tracking++].operand = null;
			codes[ldloc_s_findMemoryStream].operand = (sbyte)(tracking - ldloc_s_findMemoryStream);

			int distance = loopend - tracking;
			codes.RemoveRange(tracking, distance + 1);









			return codes.AsEnumerable();
		}
	}
}
