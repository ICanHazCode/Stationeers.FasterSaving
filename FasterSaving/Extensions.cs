using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.Utils
{
	public static class Extensions
	{
		private static readonly ConstructorInfo c_LocalBuilder = (from c in typeof(LocalBuilder)
																  .GetConstructors(BindingFlags.Instance
																					| BindingFlags.Public
																					| BindingFlags.NonPublic)
																  orderby c.GetParameters().Length descending
																  select c).First<ConstructorInfo>();

		private static readonly FieldInfo f_LocalBuilder_position = typeof(LocalBuilder).GetField("position", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly FieldInfo f_LocalBuilder_is_pinned = typeof(LocalBuilder).GetField("is_pinned", BindingFlags.Instance | BindingFlags.NonPublic);

		private static int c_LocalBuilder_params = c_LocalBuilder.GetParameters().Length;


		/// <summary>
		/// replaces a local variable in a method.
		/// Mercilessly copied from <see cref="MonoMod.Utils.Cil.CecilILGenerator"/>
		/// <seealso cref="MonoMod.Utils.Cil.CecilILGenerator"/>
		/// </summary>
		/// <param name="gen">CecilILGenerator instance</param>
		/// <param name="orig">Original Method Local variable to replace</param>
		/// <param name="type">Replacement Local variable type</param>
		/// <param name="pinned">Whether it's a pinned variable.</param>
		/// <returns></returns>
		public static LocalBuilder ReplaceLocal(this CecilILGenerator gen, KeyValuePair<LocalBuilder, VariableDefinition> orig, Type type, bool pinned = false)
		{
			//ILGenerator Variables dictionary
			Dictionary<LocalBuilder, VariableDefinition> _Variables = (Dictionary<LocalBuilder, VariableDefinition>)AccessTools
																	  .Field(typeof(CecilILGenerator), "_Variables")
																	  .GetValue(gen);
			Mono.Collections.Generic.Collection<VariableDefinition> Variables = gen.IL.Body.Variables;
			//TypeReferences
			TypeReference newTypeRef = gen.IL.Body.Method.Module.ImportReference(type);
			TypeReference oldTypeRef = orig.Value.VariableType;


			//Find original in the locals
			VariableDefinition oldVarDef = Variables[orig.Value.Index];
			int index = (oldVarDef.VariableType == oldTypeRef)
					  ? orig.Value.Index
					  : Variables.Count;

			//Create a new LocalBuilder Type
			LocalBuilder newLBHandle = (LocalBuilder)(
				c_LocalBuilder_params == 4 ? c_LocalBuilder.Invoke(new object[] { index, type, null, pinned }) :
				c_LocalBuilder_params == 3 ? c_LocalBuilder.Invoke(new object[] { index, type, null }) :
				c_LocalBuilder_params == 2 ? c_LocalBuilder.Invoke(new object[] { type, null }) :
				c_LocalBuilder_params == 0 ? c_LocalBuilder.Invoke(new object[] { }) :
				throw new NotSupportedException()
			);

			f_LocalBuilder_position?.SetValue(newLBHandle, (ushort)index);
			f_LocalBuilder_is_pinned?.SetValue(newLBHandle, pinned);

			if (pinned)
				newTypeRef = new PinnedType(newTypeRef);
			VariableDefinition newTypeDef = new VariableDefinition(newTypeRef);
			if (index >= Variables.Count)
			{
				Variables.Add(newTypeDef);
			}
			else
			{
				Variables[orig.Value.Index] = newTypeDef;
			}
			_Variables.Remove(orig.Key);
			_Variables.Add(newLBHandle, newTypeDef);
			return newLBHandle;
		}

	}
}
