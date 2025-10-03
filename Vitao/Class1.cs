using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;


namespace WildGuns.SequentialStages
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SequentialSingle : BaseUnityPlugin
    {
        public const string PluginGuid = "vitao.wgr.sequentialstages";
        public const string PluginName = "Vitao Sequential Stages";
        public const string PluginVersion = "1.0.0";

        private void Awake()
        {
            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded (single script)");
        }
    }



    internal static class ModStageList
    {
        public static readonly GameMain.StageIndex[][] StageList8 = new GameMain.StageIndex[4][]
        {
            new GameMain.StageIndex[8]
            {
                GameMain.StageIndex.St1,
                GameMain.StageIndex.St2,
                GameMain.StageIndex.St3,
                GameMain.StageIndex.St4,
                GameMain.StageIndex.St5,
                GameMain.StageIndex.St6,
                GameMain.StageIndex.St7,
                GameMain.StageIndex.St8
            },
            new GameMain.StageIndex[8]
            {
                GameMain.StageIndex.St1,
                GameMain.StageIndex.St2,
                GameMain.StageIndex.St3,
                GameMain.StageIndex.St4,
                GameMain.StageIndex.St5,
                GameMain.StageIndex.St6,
                GameMain.StageIndex.St7,
                GameMain.StageIndex.St8
            },
            new GameMain.StageIndex[8]
            {
                GameMain.StageIndex.St1,
                GameMain.StageIndex.St2,
                GameMain.StageIndex.St3,
                GameMain.StageIndex.St4,
                GameMain.StageIndex.St5,
                GameMain.StageIndex.St6,
                GameMain.StageIndex.St7,
                GameMain.StageIndex.St8
            },
            new GameMain.StageIndex[8]
            {
                GameMain.StageIndex.St1,
                GameMain.StageIndex.St2,
                GameMain.StageIndex.St3,
                GameMain.StageIndex.St4,
                GameMain.StageIndex.St5,
                GameMain.StageIndex.St6,
                GameMain.StageIndex.St7,
                GameMain.StageIndex.St8
            }
        };
    }

    [HarmonyPatch]
    internal static class StageSelect_Start_Override
    {
        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("StageSelect");
            return AccessTools.Method(type, "Start");
        }

        private static void Postfix(object __instance)
        {
            try
            {
                Type sType = __instance.GetType();
                Type gmType = AccessTools.TypeByName("GameMain");

                FieldInfo stagePanelField = AccessTools.Field(sType, "m_StagePanel");
                Array stagePanelArr = stagePanelField.GetValue(__instance) as Array;
                Type panelType = AccessTools.Inner(sType, "Panel");
                MethodInfo panelInit = AccessTools.Method(panelType, "Init", new Type[] { AccessTools.Inner(gmType, "StageIndex"), typeof(int), typeof(int), typeof(bool) });
                for (int i = 0; i < 4; i++)
                {
                    if (stagePanelArr.GetValue(i) == null)
                        stagePanelArr.SetValue(Activator.CreateInstance(panelType), i);
                }

                FieldInfo spriteMapField = AccessTools.Field(sType, "m_SpriteMap");
                Array spriteMapArr = spriteMapField.GetValue(__instance) as Array;

                int clearCount = (int)AccessTools.Field(gmType, "m_StageClearCount").GetValue(null);
                int[] clearOrder = AccessTools.Field(gmType, "m_StageClearOrder").GetValue(null) as int[];
                HashSet<int> clearedIdx = new HashSet<int>();
                if (clearOrder != null)
                {
                    for (int i = 0; i < clearCount && i < clearOrder.Length; i++)
                        clearedIdx.Add(clearOrder[i]);
                }

                // When six stages are cleared, show final stage only (vanilla flow will handle progression)
                if (clearCount >= 6)
                {
                    FieldInfo isSt1St8Field = AccessTools.Field(sType, "isSt1St8");
                    isSt1St8Field.SetValue(__instance, true);
                    for (int i = 0; i < 4; i++)
                    {
                        object panel = stagePanelArr.GetValue(i);
                        panelInit.Invoke(panel, new object[] { GameMain.StageIndex.St8, 0, 0, false });
                        object sprite = spriteMapArr.GetValue(i);
                        AccessTools.Field(sprite.GetType(), "m_SpriteNum").SetValue(sprite, 60 + ((int)GameMain.StageIndex.St8 - 1));
                        object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                        MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                        object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                        AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { false });
                    }
                    MethodInfo copyMapDummy = AccessTools.Method(sType, "CopyMapDummy");
                    if (copyMapDummy != null) copyMapDummy.Invoke(__instance, null);
                    return;
                }

                // Build remaining pool (St1..St7) via cleared order set; operate on 0-based stage indices
                List<int> remaining = new List<int>();
                for (int idx = 0; idx <= 6; idx++)
                {
                    if (!clearedIdx.Contains(idx))
                        remaining.Add(idx);
                }
                if (remaining.Count == 0)
                    remaining.Add(0);

                // Ensure grid mode (not St1/St8 auto)
                FieldInfo isSt1St8Field2 = AccessTools.Field(sType, "isSt1St8");
                isSt1St8Field2.SetValue(__instance, false);

                // Build four tiles: unique remaining first, then fill with cleared (greyed)
                List<int> tileIdx0 = new List<int>();
                foreach (var idx0 in remaining)
                {
                    if (tileIdx0.Count < 4) tileIdx0.Add(idx0);
                }
                if (tileIdx0.Count < 4)
                {
                    // gather cleared (0..6) in cleared order without duplicates
                    List<int> clearedList = new List<int>();
                    if (clearOrder != null)
                    {
                        for (int i = 0; i < clearCount && i < clearOrder.Length; i++)
                        {
                            int c = clearOrder[i];
                            if (c >= 0 && c <= 6 && !clearedList.Contains(c))
                                clearedList.Add(c);
                        }
                    }
                    foreach (var c in clearedList)
                    {
                        if (tileIdx0.Count < 4 && !tileIdx0.Contains(c))
                            tileIdx0.Add(c);
                    }
                    // Still short? fill with any remaining cleared indices (0..6) not yet used
                    for (int c = 0; tileIdx0.Count < 4 && c <= 6; c++)
                    {
                        if (!tileIdx0.Contains(c) && clearedIdx.Contains(c))
                            tileIdx0.Add(c);
                    }
                }

                // Render tiles
                Type stageEnumType = AccessTools.Inner(gmType, "StageIndex");
                for (int i = 0; i < 4; i++)
                {
                    int stageIdx0 = tileIdx0[i]; // 0..6 for St1..St7
                    bool isCleared = clearedIdx.Contains(stageIdx0);
                    object stageEnum = Enum.ToObject(stageEnumType, stageIdx0 + 1); // enum: Non=0, St1=1
                    object panel = stagePanelArr.GetValue(i);
                    panelInit.Invoke(panel, new object[] { stageEnum, 0, 0, isCleared });

                    object sprite = spriteMapArr.GetValue(i);
                    AccessTools.Field(sprite.GetType(), "m_SpriteNum").SetValue(sprite, 60 + stageIdx0);
                    object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                    MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                    object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                    AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { isCleared });
                }

                MethodInfo copyMapDummy2 = AccessTools.Method(sType, "CopyMapDummy");
                if (copyMapDummy2 != null) copyMapDummy2.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                Debug.LogError("SequentialStages: Start postfix failed: " + ex);
            }
        }
    }
}





