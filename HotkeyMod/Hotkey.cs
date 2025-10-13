using Il2Cpp;
using Il2CppTLD.Gear;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Hotkey
{
    public class HotkeyOverhaul : MelonMod
    {
        private Dictionary<KeyCode, List<string>> _hotkeyBindings = new Dictionary<KeyCode, List<string>>();
        private string _configPath = null!;
        private Dictionary<string, GearItem> _previousClothing = new Dictionary<string, GearItem>();
        private bool _debugEnabled = false;

        public override void OnApplicationStart()
        {
            MelonLogger.Msg("Hotkey Overhaul mod loaded");

            _configPath = Path.Combine(MelonEnvironment.UserDataDirectory, "tld_hotkeys.json");
            _debugEnabled = File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, "tld_hotkeys.debug"));

            LoadConfig();
        }

        public override void OnUpdate()
        {
            foreach (KeyValuePair<KeyCode, List<string>> kv in _hotkeyBindings)
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
                Dictionary<string, List<string>> defaultDict = new Dictionary<string, List<string>>
                {
                    { "F1", new List<string>{ "GEAR_Rifle_Barbs", "GEAR_Rifle_Trader", "GEAR_Rifle", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns" } },
                    { "F2", new List<string>{ "GEAR_RevolverStubNosed", "GEAR_Revolver", "GEAR_RevolverFancy", "GEAR_RevolverGreen" } },
                    { "F3", new List<string>{ "GEAR_Bow_Bushcraft", "GEAR_Bow_Woodwrights", "GEAR_Bow", "GEAR_Bow_Manufactured" } },
                    { "F4", new List<string>{ "GEAR_KeroseneLamp_Spelunkers", "GEAR_KeroseneLampB" } },
                    { "F5", new List<string>{ "GEAR_Torch" } },
                    { "F6", new List<string>{ "GEAR_MooseHideBag:1" } }
                };

                File.WriteAllText(_configPath, JsonConvert.SerializeObject(defaultDict, Formatting.Indented));
            }

            string json = File.ReadAllText(_configPath);
            Dictionary<string, List<string>> dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            _hotkeyBindings.Clear();
            foreach (KeyValuePair<string, List<string>> kv in dict)
            {
                if (Enum.TryParse<KeyCode>(kv.Key, out KeyCode key))
                    _hotkeyBindings[key] = kv.Value;
            }

            DebugLog("Config loaded with " + _hotkeyBindings.Count + " bindings");
        }

        private void ParseNameAndSlot(string raw, out string name, out int slot)
        {
            name = raw;
            slot = 0;
            if (string.IsNullOrEmpty(raw)) return;
            int idx = raw.IndexOf(':');
            if (idx >= 0)
            {
                name = raw.Substring(0, idx);
                string s = raw.Substring(idx + 1);
                if (int.TryParse(s, out int n) && (n == 1 || n == 2))
                    slot = n;
            }
        }

        private string PreviousKeyFor(ClothingRegion region, ClothingLayer layer)
        {
            return $"{(int)region}:{(int)layer}";
        }

        private void EquipBestItemFromList(List<string> gearNames)
        {
            try
            {
                Inventory inventory = GameManager.GetInventoryComponent();
                if (inventory == null)
                {
                    MelonLogger.Warning("Inventory component is null!");
                    return;
                }

                List<(GearItem gi, int slot, int priority)> candidates = new List<(GearItem gi, int slot, int priority)>();

                for (int i = 0; i < gearNames.Count; i++)
                {
                    string raw = gearNames[i];
                    ParseNameAndSlot(raw, out string cfgName, out int cfgSlot);

                    foreach (GearItemObject obj in inventory.m_Items)
                    {
                        if (obj == null) continue;
                        GearItem gi = obj.m_GearItem;
                        if (gi == null) continue;

                        if (string.Equals(gi.name, cfgName, StringComparison.OrdinalIgnoreCase) ||
                            gi.name.IndexOf(cfgName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            candidates.Add((gi, cfgSlot, i));
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    DebugLog("No matching items found in inventory!");
                    return;
                }

                // Torch handling: pick lowest HP
                List<(GearItem gi, int slot, int priority)> torchCandidates = new List<(GearItem gi, int slot, int priority)>();
                foreach ((GearItem gi, int slot, int priority) c in candidates)
                {
                    if (c.gi.name.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0)
                        torchCandidates.Add(c);
                }

                (GearItem gi, int slot, int priority) chosen;

                if (torchCandidates.Count > 0)
                {
                    float minHP = float.MaxValue;
                    (GearItem gi, int slot, int priority) best = (null, 0, int.MaxValue);
                    foreach ((GearItem gi, int slot, int priority) t in torchCandidates)
                    {
                        if (t.gi.m_CurrentHP < minHP)
                        {
                            minHP = t.gi.m_CurrentHP;
                            best = t;
                        }
                    }
                    chosen = best;
                }
                else
                {
                    int bestRounds = -1;
                    int bestPriority = int.MaxValue;
                    (GearItem gi, int slot, int priority) best = (null, 0, int.MaxValue);

                    foreach ((GearItem gi, int slot, int priority) c in candidates)
                    {
                        int rounds = 0;
                        GunItem maybeGun;
                        if (c.gi.TryGetComponent<GunItem>(out maybeGun))
                            rounds = maybeGun.NumRoundsInClip();

                        if (rounds > bestRounds || (rounds == bestRounds && c.priority < bestPriority))
                        {
                            bestRounds = rounds;
                            bestPriority = c.priority;
                            best = c;
                        }
                    }

                    chosen = best;
                }

                if (chosen.gi == null)
                {
                    MelonLogger.Warning("No suitable item found after selection!");
                    return;
                }

                GearItem bestItem = chosen.gi;
                int chosenSlot = chosen.slot;

                PlayerManager playerManager = UnityEngine.Object.FindObjectOfType<PlayerManager>();
                if (playerManager == null)
                {
                    MelonLogger.Warning("PlayerManager instance is null!");
                    return;
                }

                // CLOTHING handling
                ClothingItem clothing = bestItem.m_ClothingItem;
                if (clothing != null)
                {
                    ClothingRegion region = clothing.m_Region;
                    ClothingLayer targetLayer = chosenSlot == 2 ? clothing.m_MaxLayer : clothing.m_MinLayer;

                    string prevKey = PreviousKeyFor(region, targetLayer);

                    bool isWearingHere = false;
                    try
                    {
                        if (clothing.IsWearing() && clothing.GetEquippedLayer() == targetLayer)
                            isWearingHere = true;
                    }
                    catch { }

                    if (isWearingHere)
                    {
                        // Take off
                        playerManager.TakeOffClothingItem(bestItem);
                        try { clothing.PlayUnequipAudio(); } catch { }
                        DebugLog($"Took off clothing: {bestItem.name} (region {region}, layer {(int)targetLayer})");

                        // restore previous if any
                        GearItem prev;
                        if (_previousClothing.TryGetValue(prevKey, out prev) && prev != null)
                        {
                            ClothingItem prevCI = prev.m_ClothingItem;
                            if (prevCI != null && !prevCI.IsWearing())
                            {
                                playerManager.PutOnClothingItem(prev, targetLayer);
                                try { prevCI.PlayEquipAudio(); } catch { }
                                DebugLog($"Restored previous clothing: {prev.name} (region {region}, layer {(int)targetLayer})");
                            }
                            _previousClothing.Remove(prevKey);
                        }
                    }
                    else
                    {
                        // store currently worn
                        GearItem currentlyWorn = playerManager.GetClothingInSlot(region, targetLayer);
                        if (currentlyWorn != null && currentlyWorn != bestItem)
                        {
                            _previousClothing[prevKey] = currentlyWorn;
                            playerManager.TakeOffClothingItem(currentlyWorn);
                            DebugLog($"Stored and took off previous clothing: {currentlyWorn.name} (region {region}, layer {(int)targetLayer})");
                        }

                        // put on new
                        playerManager.PutOnClothingItem(bestItem, targetLayer);
                        try { clothing.PlayEquipAudio(); } catch { }
                        DebugLog($"Put on clothing: {bestItem.name} (region {region}, layer {(int)targetLayer})");
                    }

                    return;
                }

                // Not clothing: regular equip
                playerManager.EquipItem(bestItem, false);

                GunItem bestGun;
                if (bestItem.TryGetComponent<GunItem>(out bestGun))
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
            if (_debugEnabled)
                MelonLogger.Msg("[Hotkey DEBUG] " + msg);
        }
    }
}