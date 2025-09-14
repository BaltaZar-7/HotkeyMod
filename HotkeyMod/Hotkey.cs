using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace Hotkey
{
    public class HotkeyOverhaul : MelonMod
    {
        private Dictionary<KeyCode, List<string>> _hotkeyBindings = new Dictionary<KeyCode, List<string>>();
        private string _configPath = null!;

        public override void OnApplicationStart()
        {
            MelonLogger.Msg("Hotkey Overhaul mod loaded");

            _configPath = Path.Combine(MelonEnvironment.UserDataDirectory, "tld_hotkeys.json");
            LoadConfig();
        }

        public override void OnUpdate()
        {
            foreach (var kv in _hotkeyBindings)
            {
                if (Input.GetKeyDown(kv.Key))
                {
                    DebugLog($"Hotkey pressed: {kv.Key}, trying to equip...");
                    EquipBestItemFromList(kv.Value);
                }
            }
        }

        private void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                var defaultDict = new Dictionary<string, List<string>>
                {
                    { "F1", new List<string>{ "GEAR_Rifle_Barbs", "GEAR_Rifle_Trader", "GEAR_Rifle", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns" } },
                    { "F2", new List<string>{ "GEAR_RevolverStubNosed", "GEAR_Revolver", "GEAR_RevolverFancy", "GEAR_RevolverGreen" } },
                    { "F3", new List<string>{ "GEAR_Bow_Bushcraft", "GEAR_Bow_Woodwrights", "GEAR_Bow", "GEAR_Bow_Manufactured" } },
                    { "F4", new List<string>{ "GEAR_KeroseneLamp_Spelunkers", "GEAR_StormLantern" } },
                    { "F5", new List<string>{ "GEAR_Torch" } }
                };

                File.WriteAllText(_configPath, JsonConvert.SerializeObject(defaultDict, Formatting.Indented));
            }

            var json = File.ReadAllText(_configPath);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            _hotkeyBindings.Clear();
            foreach (var kv in dict)
            {
                if (Enum.TryParse<KeyCode>(kv.Key, out var key))
                    _hotkeyBindings[key] = kv.Value;
            }

            DebugLog("Config loaded with " + _hotkeyBindings.Count + " bindings");
        }

        private void EquipBestItemFromList(List<string> gearNames)
        {
            try
            {
                var inventory = GameManager.GetInventoryComponent();
                if (inventory == null)
                {
                    MelonLogger.Warning("Inventory component is null!");
                    return;
                }

                List<GearItem> matchingItems = new List<GearItem>();

                foreach (var obj in inventory.m_Items)
                {
                    if (obj == null) continue;
                    var gi = obj.m_GearItem;
                    if (gi == null) continue;

                    if (gearNames.Contains(gi.name))
                        matchingItems.Add(gi);
                }

                if (matchingItems.Count == 0)
                {
                    DebugLog("No matching items found in inventory!");
                    return;
                }

                GearItem bestItem = null;

                // Torch: currentHP
                List<GearItem> torches = new List<GearItem>();
                foreach (var gi in matchingItems)
                {
                    if (gi.name.Contains("Torch"))
                        torches.Add(gi);
                }

                if (torches.Count > 0)
                {
                    float minHP = float.MaxValue;
                    foreach (var t in torches)
                    {
                        if (t.m_CurrentHP < minHP)
                        {
                            minHP = t.m_CurrentHP;
                            bestItem = t;
                        }
                    }
                }
                else
                {
                    int bestRounds = -1;
                    int bestPriority = int.MaxValue;

                    foreach (var gi in matchingItems)
                    {
                        int priority = gearNames.IndexOf(gi.name);
                        int rounds = 0;

                        if (gi.TryGetComponent<GunItem>(out var gun))
                            rounds = gun.NumRoundsInClip();

                        if (rounds > bestRounds || (rounds == bestRounds && priority < bestPriority))
                        {
                            bestRounds = rounds;
                            bestPriority = priority;
                            bestItem = gi;
                        }
                    }
                }

                if (bestItem == null)
                {
                    MelonLogger.Warning("No suitable item found!");
                    return;
                }

                var playerManager = UnityEngine.Object.FindObjectOfType<PlayerManager>();
                if (playerManager == null)
                {
                    MelonLogger.Warning("PlayerManager instance is null!");
                    return;
                }

                playerManager.EquipItem(bestItem, false);

                if (bestItem.TryGetComponent<GunItem>(out var bestGun))
                    DebugLog($"Equipped {bestItem.name} with {bestGun.NumRoundsInClip()} loaded rounds");
                else
                    DebugLog($"Equipped {bestItem.name} with currentHP {bestItem.m_CurrentHP}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to equip item. Exception: {ex}");
            }
        }
        private void DebugLog(string msg)
        {
#if DEBUG
            MelonLogger.Msg(msg);
#endif
        }
    }
}
