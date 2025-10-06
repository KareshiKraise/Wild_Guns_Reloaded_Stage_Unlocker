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
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded (Harmony prefix mode)");

            // Ensure GameMain.m_StageClearOrder can hold up to 8 entries (for 8 stages)
            try
            {
                Type gmType = AccessTools.TypeByName("GameMain");
                FieldInfo orderField = AccessTools.Field(gmType, "m_StageClearOrder");
                int[] current = orderField.GetValue(null) as int[];
                if (current == null || current.Length < 8)
                {
                    int[] expanded = new int[8];
                    if (current != null)
                        Array.Copy(current, expanded, Math.Min(current.Length, expanded.Length));
                    orderField.SetValue(null, expanded);
                    Logger.LogInfo("[VITAO ]SequentialStages: Expanded GameMain.m_StageClearOrder to 8 slots in Awake()");
                }
            }
            catch (Exception e)
            {
                Logger.LogError("[VITAO ]SequentialStages: Failed to expand m_StageClearOrder: " + e);
            }
        }
    }

    [HarmonyPatch]
    internal static class EntryScore_Prefix
    {
        private static MethodBase TargetMethod()
        {
            Type gmType = AccessTools.TypeByName("GameMain");
            return AccessTools.Method(gmType, "EntryScore");
        }

        private static bool Prefix(ref int __result)
        {
            try
            {
                Type gmType = AccessTools.TypeByName("GameMain");
                // Replicate original logic but cap clear-order copy to destination length

                // Determine index1 and score based on player mode
                int startPlayerNum = (int)AccessTools.Field(gmType, "m_StartPlayerNum").GetValue(null);
                int gameDifficulty = (int)AccessTools.Field(gmType, "m_GameDifficulty").GetValue(null);

                // Get PlayerInfo[0]
                MethodInfo getPlayerInfo = AccessTools.Method(gmType, "GetPlayerInfo", new Type[] { typeof(int) });
                object pinfo0 = getPlayerInfo.Invoke(null, new object[] { 0 });
                Type chrIndexType = AccessTools.Inner(gmType, "ChrIndex");

                int index1;
                int totalScoreVal;
                if (startPlayerNum == 1)
                {
                    // Single player
                    object chrIndex = AccessTools.Field(pinfo0.GetType(), "m_ChrIndex").GetValue(pinfo0);
                    int chrIdxInt = (int)chrIndex - 1;
                    index1 = gameDifficulty * 4 + chrIdxInt;
                    object scoreScramble = AccessTools.Field(pinfo0.GetType(), "m_Score").GetValue(pinfo0);
                    totalScoreVal = (int)AccessTools.Field(scoreScramble.GetType(), "m_Value").GetValue(scoreScramble);
                }
                else if (startPlayerNum <= 0)
                {
                    __result = -1;
                    return false;
                }
                else
                {
                    // Multiplayer
                    index1 = 16 + (startPlayerNum - 2);
                    object totalScramble = AccessTools.Field(gmType, "m_TotalScore").GetValue(null);
                    totalScoreVal = (int)AccessTools.Field(totalScramble.GetType(), "m_Value").GetValue(totalScramble);
                }

                // Common.m_SaveData.m_ScoreEntry[index1]
                Type commonType = AccessTools.TypeByName("Common");
                object saveData = AccessTools.Field(commonType, "m_SaveData").GetValue(null);
                // Access array element properly via reflection
                Array scoreEntryArr = AccessTools.Field(saveData.GetType(), "m_ScoreEntry").GetValue(saveData) as Array;
                GameMain.ScoreEntry se = (GameMain.ScoreEntry)scoreEntryArr.GetValue(index1);

                // Write fields
                se.m_Registrable = true;
                se.m_Score = totalScoreVal;

                int stageClearCount = (int)AccessTools.Field(gmType, "m_StageClearCount").GetValue(null);
                se.m_ClearCount = stageClearCount;

                int[] clearOrderSrc = (int[])AccessTools.Field(gmType, "m_StageClearOrder").GetValue(null);
                int copyLen = Math.Min(clearOrderSrc.Length, se.m_ClearOrder.Length);
                for (int i = 0; i < copyLen; ++i)
                    se.m_ClearOrder[i] = clearOrderSrc[i];

                // Characters/colors
                for (int i = 0; i < 4; ++i)
                {
                    object pinfo = getPlayerInfo.Invoke(null, new object[] { i });
                    se.m_Character[i] = (GameMain.ChrIndex)AccessTools.Field(pinfo.GetType(), "m_ChrIndex").GetValue(pinfo);
                    se.m_CharacterCol[i] = (int)AccessTools.Field(pinfo.GetType(), "m_Color").GetValue(pinfo);
                }

                se.m_SingleDifficulty = gameDifficulty;
                se.m_MultiPlayerNum = startPlayerNum;
                for (int i = 0; i < 2; ++i)
                {
                    se.m_Ranks[i] = 0;
                    se.m_RecordDates[i] = 0UL;
                }

                __result = index1;
                return false; // skip original
            }
            catch (Exception ex)
            {
                Debug.LogError("[VITAO ]SequentialStages: EntryScore prefix failed: " + ex);
                return true; // fallback to original
            }
        }
    }

    [HarmonyPatch]
    internal static class ExpandStageClearOrder
    {
        // Target the GameMain class static constructor to expand the array on load
        static MethodBase TargetMethod()
        {
            Type gmType = AccessTools.TypeByName("GameMain");
            return AccessTools.Constructor(gmType, Type.EmptyTypes, true); // static ctor
        }

        static void Postfix()
        {
            Type gmType = AccessTools.TypeByName("GameMain");
            FieldInfo orderField = AccessTools.Field(gmType, "m_StageClearOrder");
            int[] oldArray = orderField.GetValue(null) as int[];

            // If null or shorter than 8, replace with new array of length 8
            if (oldArray == null || oldArray.Length < 8)
            {
                orderField.SetValue(null, new int[8]);
                Debug.Log("[VITAO ]SequentialStages: Expanded GameMain.m_StageClearOrder to 8 slots");
            }
        }
    }



    [HarmonyPatch]
    internal static class StageSelect_Start_Prefix
    {
        internal static class ModStageList
        {
            public static readonly GameMain.StageIndex[] stageList8 =

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

            };
        }

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("StageSelect");
            return AccessTools.Method(type, "Start");
        }

        private static bool Prefix(object __instance)
        {
            try
            {
                Type sType = __instance.GetType();
                Type gmType = AccessTools.TypeByName("GameMain");

                // --- RefreshRoll(StageSelect.m_CylinderAgl)
                MethodInfo refreshRoll = AccessTools.Method(sType, "RefreshRoll", new Type[] { typeof(float) });
                FieldInfo cylField = AccessTools.Field(sType, "m_CylinderAgl");
                float cyl = (float)cylField.GetValue(null);
                if (refreshRoll != null) refreshRoll.Invoke(__instance, new object[] { cyl });

                // m_StageOfs = 0
                AccessTools.Field(sType, "m_StageOfs").SetValue(__instance, 0);

                // Ensure m_StagePanel[4] instances and get Panel.Init
                FieldInfo stagePanelField = AccessTools.Field(sType, "m_StagePanel");
                Array stagePanelArr = stagePanelField.GetValue(__instance) as Array;
                Type panelType = AccessTools.Inner(sType, "Panel");
                MethodInfo panelInit = AccessTools.Method(panelType, "Init", new Type[] { AccessTools.Inner(gmType, "StageIndex"), typeof(int), typeof(int), typeof(bool) });
                for (int i = 0; i < 4; i++)
                {
                    if (stagePanelArr.GetValue(i) == null)
                        stagePanelArr.SetValue(Activator.CreateInstance(panelType), i);
                }

                // m_SpriteMap[]
                FieldInfo spriteMapField = AccessTools.Field(sType, "m_SpriteMap");
                Array spriteMapArr = spriteMapField.GetValue(__instance) as Array;

                // GameMain.isStageClear
                bool[] isStageClear = AccessTools.Field(gmType, "isStageClear").GetValue(null) as bool[];
                if (isStageClear == null)
                {
                    Debug.LogError("[VITAO ]SequentialStages: isStageClear is null, defaulting to original Start method");
                    return true; // fallback to original if something is wrong
                }

                // if (!isStageClear[0]) => St1-only
                if (!isStageClear[0])
                {
                    AccessTools.Field(sType, "m_StageOfs").SetValue(__instance, 0);
                    AccessTools.Field(sType, "isSt1St8").SetValue(__instance, true);
                    for (int index = 0; index < 4; ++index)
                    {
                        object panel = stagePanelArr.GetValue(index);
                        panelInit.Invoke(panel, new object[] { ModStageList.stageList8[0], 0, 0, false });
                        object sprite = spriteMapArr.GetValue(index);
                        object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                        MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                        object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                        AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { false });
                    }
                }
                else
                {
                    // Count ClearNum for St2..St7 and record cleared flags
                    int ClearNum = 0;
                    bool[] clearedStages = new bool[6];
                    for (int i = 1; i <= 6; ++i)
                    {
                        clearedStages[i - 1] = isStageClear[i];
                        if (clearedStages[i - 1])
                            ClearNum++;
                    }

                    if (ClearNum == 6)
                    {
                        // All middle stages cleared -> force St8
                        AccessTools.Field(sType, "m_StageOfs").SetValue(__instance, 0);
                        AccessTools.Field(sType, "isSt1St8").SetValue(__instance, true);
                        for (int index = 0; index < 4; ++index)
                        {
                            object panel = stagePanelArr.GetValue(index);
                            panelInit.Invoke(panel, new object[] { ModStageList.stageList8[7], 0, 0, false });
                            object sprite = spriteMapArr.GetValue(index);
                            object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                            MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                            object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                            AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { false });
                        }
                    }
                    else
                    {
                        // Build remaining list (St2..St7) in ascending order of indices
                        List<int> remainingList = new List<int>();
                        for (int i = 1; i <= 6; ++i)
                        {
                            if (!isStageClear[i])
                                remainingList.Add(i); // store indices 1..6 representing St2..St7
                        }

                        // Find last cleared middle stage index (for greyed-out sprite fallback)
                        int lastCleared = 1; // default fallback to St2 (index 1) if none are cleared
                        for (int i = 1; i <= 6; ++i)
                        {
                            if (isStageClear[i])
                                lastCleared = i;
                        }

                        // Fill up to 4 slots dynamically
                        for (int panelIndex = 0; panelIndex < 4; ++panelIndex)
                        {
                            int stageToShowIndex; // 1..6 indexes into stageList8
                            bool slotIsGrey = false;
                            bool slotIsMarkedClearParam = false; // parameter passed to Panel.Init's isClear

                            if (panelIndex < remainingList.Count)
                            {
                                // Active slot -> show the next remaining stage (never mark panel as clear)
                                stageToShowIndex = remainingList[panelIndex];
                                slotIsGrey = false;
                                slotIsMarkedClearParam = false;
                            }
                            else
                            {
                                // No remaining stage for this slot -> show greyed-out sprite of last cleared middle stage
                                stageToShowIndex = lastCleared;
                                slotIsGrey = true;
                                slotIsMarkedClearParam = true;
                            }

                            // Clamp safety
                            if (stageToShowIndex < 1) stageToShowIndex = 1;
                            if (stageToShowIndex > 6) stageToShowIndex = 6;

                            int myClearNum = 0;
                            if (ClearNum >= 3) myClearNum = 3;
                            else myClearNum = ClearNum;

                            // panelInit expects a GameMain.StageIndex value from stageList8 (0-based array),
                            // our indices are 1..6 corresponding to St2..St7, so use that index directly.
                            object panel = stagePanelArr.GetValue(panelIndex);
                            panelInit.Invoke(panel, new object[] { ModStageList.stageList8[stageToShowIndex], myClearNum, 0, slotIsMarkedClearParam });


                            // --- Update sprite map (cylinder display) like vanilla ---
                            object sprite = spriteMapArr.GetValue(panelIndex);
                            // Set the sprite number based on panel.m_Index
                            int panelIndexValue = (int)AccessTools.Field(panel.GetType(), "m_Index").GetValue(panel);
                            AccessTools.Field(sprite.GetType(), "m_SpriteNum").SetValue(sprite, 60 + panelIndexValue);

                            // Update gray-out status
                            object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                            MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                            object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                            AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { slotIsGrey });

                            // Set Map gray state
                            //object sprite = spriteMapArr.GetValue(panelIndex);
                            //object go = AccessTools.Property(sprite.GetType(), "gameObject").GetValue(sprite, null);
                            //MethodInfo getComponent = AccessTools.Method(go.GetType(), "GetComponent", new Type[] { typeof(Type) });
                            //object mapComp = getComponent.Invoke(go, new object[] { AccessTools.TypeByName("StageSelectMap") });
                            //AccessTools.Method(mapComp.GetType(), "SetGrayOut", new Type[] { typeof(bool) }).Invoke(mapComp, new object[] { slotIsGrey });
                        }
                    }
                }

                // CopyMapDummy()
                MethodInfo copyMapDummy = AccessTools.Method(sType, "CopyMapDummy");
                if (copyMapDummy != null) copyMapDummy.Invoke(__instance, null);

                // Player faces
                Array playerFaceArr = AccessTools.Field(sType, "m_PlayerFace").GetValue(__instance) as Array;
                MethodInfo getPlayerInfo = AccessTools.Method(gmType, "GetPlayerInfo", new Type[] { typeof(int) });
                Type chrIndexType = AccessTools.Inner(gmType, "ChrIndex");
                for (int Index = 0; Index < 4; ++Index)
                {
                    object face = playerFaceArr.GetValue(Index);
                    object pinfo = getPlayerInfo.Invoke(null, new object[] { Index });
                    object chrIdx = AccessTools.Field(pinfo.GetType(), "m_ChrIndex").GetValue(pinfo);
                    int color = (int)AccessTools.Field(pinfo.GetType(), "m_Color").GetValue(pinfo);
                    bool isNon = chrIdx.Equals(Enum.ToObject(chrIndexType, 0));
                    AccessTools.Property(face.GetType(), "enabled").SetValue(face, !isNon, null);
                    if (!isNon)
                    {
                        AccessTools.Field(face.GetType(), "m_SpriteNum").SetValue(face, 96 + ((int)chrIdx - 1));
                        AccessTools.Field(face.GetType(), "m_Atb").SetValue(face, color * 2);
                    }
                }

                // NumRollStop(4 - (int) m_CylinderAgl & 3)
                MethodInfo numRollStop = AccessTools.Method(sType, "NumRollStop", new Type[] { typeof(int) });
                int panelNum = (4 - (int)cyl) & 3;
                if (numRollStop != null) numRollStop.Invoke(__instance, new object[] { panelNum });

                // if (!isSt1St8) return; else start CloseSeq and set Wait
                bool isSt1St8 = (bool)AccessTools.Field(sType, "isSt1St8").GetValue(__instance);
                if (isSt1St8)
                {
                    MethodInfo startCoroutine = AccessTools.Method(sType, "StartCoroutine", new Type[] { typeof(string) });
                    if (startCoroutine != null) startCoroutine.Invoke(__instance, new object[] { "CloseSeq" });
                    AccessTools.Field(sType, "m_Step").SetValue(__instance, Enum.ToObject(AccessTools.Inner(sType, "Step"), 3)); // Wait
                }

                Debug.Log("[VITAO ]SequentialStages: Start method ran fine");
                return false; // skip original Start
            }
            catch (Exception ex)
            {
                Debug.LogError("SequentialStages: Start prefix failed: " + ex);
                return true; // fall back to original
            }
        }
    }
}
