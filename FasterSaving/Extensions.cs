using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Utils.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
//TODO: Move this to it's own library
namespace ICanHazCode.Utils
{
	/// <summary>
	/// Extension utilities for BepInEx transpiler modding
	/// </summary>
	public static class Extensions
	{
		#region Shameless copy from CecilILGenerator
		private static readonly ConstructorInfo c_LocalBuilder = (from c in typeof(LocalBuilder)
																  .GetConstructors(BindingFlags.Instance
																					| BindingFlags.Public
																					| BindingFlags.NonPublic)
																  orderby c.GetParameters().Length descending
																  select c).First<ConstructorInfo>();

		private static readonly FieldInfo f_LocalBuilder_position = typeof(LocalBuilder).GetField("position", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly FieldInfo f_LocalBuilder_is_pinned = typeof(LocalBuilder).GetField("is_pinned", BindingFlags.Instance | BindingFlags.NonPublic);

		private static int c_LocalBuilder_params = c_LocalBuilder.GetParameters().Length;
		#endregion

		public static Dictionary<LocalBuilder, VariableDefinition> GetGenDictionary(this ILGenerator gen)
		{
			CecilILGenerator CecilGen = gen.GetProxiedShim<CecilILGenerator>();
			return (Dictionary<LocalBuilder, VariableDefinition>)AccessTools
																	.Field(typeof(CecilILGenerator), "_Variables")
																	.GetValue(CecilGen);
		}

		public static Collection<VariableDefinition> GetGenVariables(this ILGenerator gen)
			=> gen.GetProxiedShim<CecilILGenerator>().IL.Body.Variables;

		public static CecilILGenerator GetCecilGen(this ILGenerator gen)
		   => gen.GetProxiedShim<CecilILGenerator>();

		public static ModuleDefinition GetGenModule(this ILGenerator gen) => gen.GetCecilGen().IL.Body.Method.Module;

		public static IList<LocalBuilder> GetLocalBuilders(this ILGenerator gen)
			=> GetGenDictionary(gen).Keys.ToList();

		/// <summary>
		/// replaces a local variable in a method.
		/// Mercilessly copied from <see cref="MonoMod.Utils.Cil.CecilILGenerator"/>
		/// <seealso cref="MonoMod.Utils.Cil.CecilILGenerator"/>
		/// </summary>
		/// <param name="gen">ILGenerator instance</param>
		/// <param name="orig">Original Method Local variable to replace</param>
		/// <param name="type">Replacement Local variable type</param>
		/// <param name="pinned">Whether it's a pinned variable.</param>
		/// <returns></returns>
		public static LocalBuilder ReplaceLocal(this ILGenerator gen, VariableDefinition orig, Type type, bool pinned = false)
		{

			//Declare a new local variable in the method
			LocalBuilder newLocal = gen.DeclareLocal(type, pinned);
			//ILGenerator Variables dictionary
			Dictionary<LocalBuilder, VariableDefinition> _Variables = gen.GetGenDictionary();
			Collection<VariableDefinition> VariableDefs = gen.GetGenVariables();
			// Find old Local Pair
			KeyValuePair<LocalBuilder, VariableDefinition> KVPairOld = _Variables.First((x) => x.Value.Index == orig.Index);
			VariableDefinition newLocalDef = _Variables[newLocal];
			//save the old index
			int index = KVPairOld.Value.Index;
			//Remove the old local definition from the Dictionary and List
			_Variables.Remove(KVPairOld.Key);
			VariableDefs.Remove(KVPairOld.Value);
			//Remove the newly created localDef so we can transplant it
			VariableDefs.Remove(newLocalDef);
			//Place it in the old location
			VariableDefs.Insert(index, _Variables[newLocal]);
			// Return the reference to be used with the transpiler
			return newLocal;
		}
	}
}
