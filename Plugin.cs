using BepInEx;
using HarmonyLib;
using FixSeeCorpse.Helpers;
using System.Collections;
using UnityEngine;

namespace FixSeeCorpse
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.Krokosha.KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.user.FixSeeCorpse";
        public const string Name = "FixSeeCorpse";
        public const string Version = "1.8.3";

        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("FixSeeCorpse loaded!");

            _harmony = new Harmony(Guid);
            
            CustomPatcher.PatchByName(_harmony, typeof(MyPatches));

            StartCoroutine(DelayedMultiplayerPatch());
        }

        private IEnumerator DelayedMultiplayerPatch()
        {

            yield return new WaitForSeconds(0.5f);
            var mpType = AccessTools.TypeByName("KrokoshaCasualtiesMP.Krokosha_CorpseScript_MultiplayerAdditionComponent");
            
            if (mpType != null)
            {
                Log.LogInfo("Multiplayer mod detected! Patching Krokosha_CorpseScript_MultiplayerAdditionComponent...");
                CustomPatcher.PatchMultiplayerClass(_harmony, typeof(MyPatches));
            }
            else
            {
                Log.LogWarning("Multiplayer mod not found. Skipping multiplayer patches.");
            }
        }
    }
}