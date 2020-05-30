using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;

namespace stationeers.fastersaving
{
	[BepInPlugin("net.icanhazcode.stationeers.fastersaving", "FasterSaving", "0.2.0")]
	public class PatchWriteWorld : BaseUnityPlugin
	{
		public static PatchWriteWorld Instance;
		public static bool Debug = false;


		public void Log(string line)
		{
			Logger.LogInfo(line);
		}

		public void LogError(string error)
		{
			Logger.LogError(error);
		}

		void Awake()
		{
			Instance = this;
			Debug = Config.Bind(new ConfigDefinition("Logging", "Debug"),
							false,
							new ConfigDescription("Logs transpiler fixes and resultant code")).Value;
			try
			{
				Log("Patching WriteWorld()");
				Harmony harmony = new Harmony("net.icanhazcode.stationeers.fastersaving");
				harmony.PatchAll();

				Log("Patched ok.");
			}
			catch (Exception e)
			{
				LogError("Patch failure.");
				LogError(e.ToString());
				throw e;

			}
		}
	}
}
