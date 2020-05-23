using BepInEx;
using HarmonyLib;
using System;
using UnityEngine;

namespace net.icanhazcode.stationeers.fastersaving
{
	[BepInPlugin("net.icanhazcode.stationeers.fastersaving", "Save speed improvements", "1.0.0.0")]
	public class PatchWriteWorld : BaseUnityPlugin
	{
		public static PatchWriteWorld Instance;

		public void Log(string line)
		{
			Debug.Log("[PatchWriteWorld]:" + line);
		}
		void Awake()
		{
			Instance = this;
			try
			{
				Harmony harmony = new Harmony("net.icanhazcode.stationeers.fastersaving");
				harmony.PatchAll();
				Log("Patched ok.");
			}
			catch (Exception e)
			{
				Log("Patch failure.");
				Log(e.ToString());

			}
		}


	}
}
