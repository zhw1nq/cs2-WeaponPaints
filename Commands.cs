using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using Menu;
using Menu.Enums;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void OnCommandRefresh(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled || !_gBCommandsAllowed) return;
        if (!Utility.IsPlayerValid(player)) return;

        if (player == null || !player.IsValid || player.UserId == null || player.IsBot) return;

        PlayerInfo? playerInfo = new PlayerInfo
        {
            UserId = player.UserId,
            Slot = player.Slot,
            Index = (int)player.Index,
            SteamId = player?.SteamID.ToString(),
            Name = player?.PlayerName,
            IpAddress = player?.IpAddress?.Split(":")[0]
        };

        try
        {
            if (player != null && !CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                player != null && DateTime.UtcNow >= (CommandsCooldown.TryGetValue(player.Slot, out cooldownEndTime) ? cooldownEndTime : DateTime.UtcNow))
            {
                CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                if (WeaponSync != null)
                {
                    // Call sync method - it handles ThreadPool internally
                    WeaponSync.GetPlayerData(playerInfo);

                    GivePlayerGloves(player);
                    RefreshWeapons(player);
                    GivePlayerAgent(player);
                    GivePlayerMusicKit(player);
                    AddTimer(0.15f, () => GivePlayerPin(player));
                }

                if (!string.IsNullOrEmpty(Localizer["wp_command_refresh_done"]))
                {
                    player.Print(Localizer["wp_command_refresh_done"]);
                }
                return;
            }
            if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
            {
                player!.Print(Localizer["wp_command_cooldown"]);
            }
        }
        catch (Exception) { }
    }

    private void OnCommandWS(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.Additional.SkinEnabled) return;
        if (!Utility.IsPlayerValid(player)) return;

        if (!string.IsNullOrEmpty(Localizer["wp_info_website"]))
        {
            player!.Print(Localizer["wp_info_website", Config.Website]);
        }
        if (!string.IsNullOrEmpty(Localizer["wp_info_refresh"]))
        {
            player!.Print(Localizer["wp_info_refresh"]);
        }

        if (Config.Additional.GloveEnabled)
            if (!string.IsNullOrEmpty(Localizer["wp_info_glove"]))
            {
                player!.Print(Localizer["wp_info_glove"]);
            }

        if (Config.Additional.AgentEnabled)
            if (!string.IsNullOrEmpty(Localizer["wp_info_agent"]))
            {
                player!.Print(Localizer["wp_info_agent"]);
            }

        if (Config.Additional.MusicEnabled)
            if (!string.IsNullOrEmpty(Localizer["wp_info_music"]))
            {
                player!.Print(Localizer["wp_info_music"]);
            }

        if (Config.Additional.PinsEnabled)
            if (!string.IsNullOrEmpty(Localizer["wp_info_pin"]))
            {
                player!.Print(Localizer["wp_info_pin"]);
            }

        if (!Config.Additional.KnifeEnabled) return;
        if (!string.IsNullOrEmpty(Localizer["wp_info_knife"]))
        {
            player!.Print(Localizer["wp_info_knife"]);
        }
    }

    private void RegisterCommands()
    {
        _config.Additional.CommandStattrak.ForEach(c =>
        {
            AddCommand($"css_{c}", "Stattrak toggle", (player, info) =>
            {
                if (!Utility.IsPlayerValid(player)) return;

                OnCommandStattrak(player, info);
            });
        });

        _config.Additional.CommandSkin.ForEach(c =>
        {
            AddCommand($"css_{c}", "Skins info", (player, info) =>
            {
                if (!Utility.IsPlayerValid(player)) return;
                OnCommandWS(player, info);
            });
        });

        _config.Additional.CommandRefresh.ForEach(c =>
        {
            AddCommand($"css_{c}", "Skins refresh", (player, info) =>
            {
                if (!Utility.IsPlayerValid(player)) return;
                OnCommandRefresh(player, info);
            });
        });

        if (Config.Additional.CommandKillEnabled)
        {
            _config.Additional.CommandKill.ForEach(c =>
            {
                AddCommand($"css_{c}", "kill yourself", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid) return;

                    player.PlayerPawn.Value.CommitSuicide(true, false);
                });
            });
        }

        // Register menu commands with KitsuneMenu
        RegisterMenuCommands();
    }

    private void RegisterMenuCommands()
    {
        // Register knife commands
        if (Config.Additional.KnifeEnabled)
        {
            _config.Additional.CommandKnife.ForEach(c =>
            {
                AddCommand($"css_{c}", "Knife Menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenKnifeMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }

        // Register skin selection commands
        if (Config.Additional.SkinEnabled)
        {
            _config.Additional.CommandSkinSelection.ForEach(c =>
            {
                AddCommand($"css_{c}", "Skins selection menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenSkinsMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }

        // Register glove commands
        if (Config.Additional.GloveEnabled)
        {
            _config.Additional.CommandGlove.ForEach(c =>
            {
                AddCommand($"css_{c}", "Gloves selection menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenGlovesMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }

        // Register agent commands
        if (Config.Additional.AgentEnabled)
        {
            _config.Additional.CommandAgent.ForEach(c =>
            {
                AddCommand($"css_{c}", "Agents selection menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenAgentsMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }

        // Register music commands
        if (Config.Additional.MusicEnabled)
        {
            _config.Additional.CommandMusic.ForEach(c =>
            {
                AddCommand($"css_{c}", "Music selection menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenMusicMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }

        // Register pin commands
        if (Config.Additional.PinsEnabled)
        {
            _config.Additional.CommandPin.ForEach(c =>
            {
                AddCommand($"css_{c}", "Pin selection menu", (player, _) =>
                {
                    if (player == null || !Utility.IsPlayerValid(player) || Menu == null) return;
                    if (!_gBCommandsAllowed) return;

                    if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) ||
                        DateTime.UtcNow >= cooldownEndTime)
                    {
                        CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);
                        OpenPinsMenu(player);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                            player.Print(Localizer["wp_command_cooldown"]);
                    }
                });
            });
        }
    }

    #region KitsuneMenu Methods

    private void OpenKnifeMenu(CCSPlayerController player)
    {
        var knivesOnly = WeaponList
            .Where(pair => pair.Key.StartsWith("weapon_knife") || pair.Key.StartsWith("weapon_bayonet"))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, string>();
        int i = 0;

        foreach (var knifePair in knivesOnly)
        {
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(knifePair.Value)]));
            optionMap[i++] = knifePair.Key;
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_knife_menu_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var knifeKey)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                var playerKnives = GPlayersKnife.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, string>());
                var teamsToCheck = player.TeamNum < 2
                    ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist }
                    : [player.Team];

                var knifeName = knivesOnly[knifeKey];
                if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_select"]))
                    player.Print(Localizer["wp_knife_menu_select", knifeName]);

                if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_kill"]) && Config.Additional.CommandKillEnabled)
                    player.Print(Localizer["wp_knife_menu_kill"]);

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                foreach (var team in teamsToCheck)
                    playerKnives[team] = knifeKey;

                if (_gBCommandsAllowed && (LifeState_t)player.LifeState == LifeState_t.LIFE_ALIVE)
                    RefreshWeapons(player);

                if (WeaponSync != null)
                    _ = Task.Run(async () => await WeaponSync.SyncKnifeToDatabase(playerInfo, knifeKey, teamsToCheck));
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenSkinsMenu(CCSPlayerController player)
    {
        var classNamesByWeapon = WeaponList
            .Except([new KeyValuePair<string, string>("weapon_knife", "Default Knife")])
            .Where(kvp => !kvp.Key.StartsWith("weapon_knife") && !kvp.Key.StartsWith("weapon_bayonet"))
            .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, string>();
        int i = 0;

        foreach (var weaponName in classNamesByWeapon.Keys)
        {
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(weaponName)]));
            optionMap[i++] = weaponName;
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_skin_menu_weapon_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var selectedWeapon)) return;
                if (!Utility.IsPlayerValid(player)) return;
                if (!classNamesByWeapon.TryGetValue(selectedWeapon, out var selectedWeaponClassname)) return;

                OpenSkinSubMenu(player, selectedWeapon, selectedWeaponClassname);
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenSkinSubMenu(CCSPlayerController player, string weaponDisplayName, string weaponClassname)
    {
        var skinsForSelectedWeapon = SkinsList.Where(skin =>
            skin.TryGetValue("weapon_name", out var weaponName) &&
            weaponName?.ToString() == weaponClassname
        ).ToList();

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, (int paintId, int weaponDefIndex)>();
        int i = 0;

        foreach (var skin in skinsForSelectedWeapon)
        {
            if (!skin.TryGetValue("paint_name", out var paintNameObj) ||
                !skin.TryGetValue("paint", out var paintObj) ||
                !skin.TryGetValue("weapon_defindex", out var defIndexObj)) continue;

            var paintName = paintNameObj?.ToString();
            var paint = paintObj?.ToString();
            if (!int.TryParse(paint, out var paintId)) continue;
            if (!int.TryParse(defIndexObj?.ToString(), out var weaponDefIndex)) continue;

            if (!string.IsNullOrEmpty(paintName) && !string.IsNullOrEmpty(paint))
            {
                items.Add(new MenuItem(MenuItemType.Button, [new MenuValue($"{paintName} ({paint})")]));
                optionMap[i++] = (paintId, weaponDefIndex);
            }
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_skin_menu_skin_title", weaponDisplayName],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var skinData)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                var (paintId, weaponDefIndex) = skinData;

                if (Config.Additional.ShowSkinImage)
                {
                    var foundSkin = SkinsList.FirstOrDefault(skin =>
                        ((int?)skin["weapon_defindex"] ?? 0) == weaponDefIndex &&
                        ((int?)skin["paint"] ?? 0) == paintId &&
                        skin["image"] != null
                    );
                    var image = foundSkin?["image"]?.ToString() ?? "";
                    _playerWeaponImage[player.Slot] = image;
                    AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
                }

                player.Print(Localizer["wp_skin_menu_select", weaponDisplayName]);

                var playerSkins = GPlayerWeaponsInfo.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());
                var teamsToCheck = player.TeamNum < 2
                    ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist }
                    : [player.Team];

                foreach (var team in teamsToCheck)
                {
                    var teamWeapons = playerSkins.GetOrAdd(team, _ => new ConcurrentDictionary<int, WeaponInfo>());
                    var value = teamWeapons.GetOrAdd(weaponDefIndex, _ => new WeaponInfo());
                    value.Paint = paintId;
                    value.Wear = 0.01f;
                    value.Seed = 0;
                }

                var playerInfo = new PlayerInfo
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                if (_gBCommandsAllowed && (LifeState_t)player.LifeState == LifeState_t.LIFE_ALIVE && WeaponSync != null)
                {
                    RefreshWeapons(player);
                    _ = Task.Run(async () => await WeaponSync.SyncWeaponPaintsToDatabase(playerInfo));
                }
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenGlovesMenu(CCSPlayerController player)
    {
        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, (int weaponDefindex, int paint, string image)>();
        int i = 0;

        // Add "None" option
        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["None"])]));
        optionMap[i++] = (0, 0, "");

        foreach (var gloveObject in GlovesList)
        {
            var paintName = gloveObject["paint_name"]?.ToString() ?? "";
            if (paintName.Length == 0) continue;

            if (!gloveObject.ContainsKey("weapon_defindex") || !gloveObject.ContainsKey("paint")) continue;
            if (!int.TryParse(gloveObject["weapon_defindex"]?.ToString(), out var weaponDefindex)) continue;
            if (!int.TryParse(gloveObject["paint"]?.ToString(), out var paint)) continue;

            var image = gloveObject["image"]?.ToString() ?? "";
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(paintName)]));
            optionMap[i++] = (weaponDefindex, paint, image);
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_glove_menu_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var gloveData)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                var (weaponDefindex, paint, image) = gloveData;
                var playerGloves = GPlayersGlove.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ushort>());
                var teamsToCheck = player.TeamNum < 2
                    ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist }
                    : [player.Team];

                if (Config.Additional.ShowSkinImage && !string.IsNullOrEmpty(image))
                {
                    _playerWeaponImage[player.Slot] = image;
                    AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
                }

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                if (paint != 0)
                {
                    if (!GPlayerWeaponsInfo.ContainsKey(player.Slot))
                        GPlayerWeaponsInfo[player.Slot] = new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>();

                    foreach (var team in teamsToCheck)
                    {
                        if (!GPlayerWeaponsInfo[player.Slot].ContainsKey(team))
                            GPlayerWeaponsInfo[player.Slot][team] = new ConcurrentDictionary<int, WeaponInfo>();

                        playerGloves[team] = (ushort)weaponDefindex;

                        if (!GPlayerWeaponsInfo[player.Slot][team].ContainsKey(weaponDefindex))
                        {
                            WeaponInfo weaponInfo = new() { Paint = paint };
                            GPlayerWeaponsInfo[player.Slot][team][weaponDefindex] = weaponInfo;
                        }
                    }
                }
                else
                {
                    GPlayersGlove.TryRemove(player.Slot, out _);
                }

                if (WeaponSync != null)
                {
                    _ = Task.Run(async () =>
                    {
                        foreach (var team in teamsToCheck)
                        {
                            await WeaponSync.SyncGloveToDatabase(playerInfo, (ushort)weaponDefindex, teamsToCheck);

                            if (!GPlayerWeaponsInfo[playerInfo.Slot][team].TryGetValue(weaponDefindex, out var value))
                            {
                                value = new WeaponInfo();
                                GPlayerWeaponsInfo[playerInfo.Slot][team][weaponDefindex] = value;
                            }

                            value.Paint = paint;
                            value.Wear = 0.00f;
                            value.Seed = 0;

                            await WeaponSync.SyncWeaponPaintsToDatabase(playerInfo);
                        }
                    });
                }

                AddTimer(0.1f, () => GivePlayerGloves(player));
                AddTimer(0.25f, () => GivePlayerGloves(player));
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenAgentsMenu(CCSPlayerController player)
    {
        var filteredAgents = AgentsList.Where(agentObject =>
        {
            if (agentObject["team"]?.Value<int>() is { } teamNum)
                return teamNum == player.TeamNum;
            return false;
        }).ToList();

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, JObject>();
        int i = 0;

        // Add "None" option
        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["None"])]));
        optionMap[i++] = null!;

        foreach (var agentObject in filteredAgents)
        {
            var paintName = agentObject["agent_name"]?.ToString() ?? "";
            if (paintName.Length > 0)
            {
                items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(paintName)]));
                optionMap[i++] = agentObject;
            }
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_agent_menu_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var selectedAgent)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                if (selectedAgent == null)
                {
                    // Reset agent
                    if (player.TeamNum == 3)
                        GPlayersAgent.AddOrUpdate(player.Slot, _ => (null, null), (_, oldValue) => (null, oldValue.T));
                    else
                        GPlayersAgent.AddOrUpdate(player.Slot, _ => (null, null), (_, oldValue) => (oldValue.CT, null));

                    if (!string.IsNullOrEmpty(Localizer["wp_agent_menu_select"]))
                        player.Print(Localizer["wp_agent_menu_select", Localizer["None"]]);

                    if (WeaponSync != null)
                        _ = Task.Run(async () => await WeaponSync.SyncAgentToDatabase(playerInfo));
                    return;
                }

                if (selectedAgent.ContainsKey("model"))
                {
                    var selectedPaintName = selectedAgent["agent_name"]?.ToString() ?? "";

                    if (Config.Additional.ShowSkinImage)
                    {
                        var image = selectedAgent["image"]?.ToString() ?? "";
                        _playerWeaponImage[player.Slot] = image;
                        AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
                    }

                    if (!string.IsNullOrEmpty(Localizer["wp_agent_menu_select"]))
                        player.Print(Localizer["wp_agent_menu_select", selectedPaintName]);

                    var modelValue = selectedAgent["model"]!.ToString();
                    var modelOrNull = modelValue.Equals("null") ? null : modelValue;

                    if (player.TeamNum == 3)
                        GPlayersAgent.AddOrUpdate(player.Slot, _ => (modelOrNull, null), (_, oldValue) => (modelOrNull, oldValue.T));
                    else
                        GPlayersAgent.AddOrUpdate(player.Slot, _ => (null, modelOrNull), (_, oldValue) => (oldValue.CT, modelOrNull));

                    if (WeaponSync != null)
                        _ = Task.Run(async () => await WeaponSync.SyncAgentToDatabase(playerInfo));
                }
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenMusicMenu(CCSPlayerController player)
    {
        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, (int id, string image)>();
        int i = 0;

        // Add "None" option
        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["None"])]));
        optionMap[i++] = (0, "");

        foreach (var musicObject in MusicList)
        {
            var paintName = musicObject["name"]?.ToString() ?? "";
            if (paintName.Length == 0) continue;

            if (!musicObject.ContainsKey("id")) continue;
            if (!int.TryParse(musicObject["id"]?.ToString(), out var id)) continue;

            var image = musicObject["image"]?.ToString() ?? "";
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(paintName)]));
            optionMap[i++] = (id, image);
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_music_menu_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var musicData)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                var (paint, image) = musicData;
                var playerMusic = GPlayersMusic.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ushort>());
                var teamsToCheck = player.TeamNum < 2
                    ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist }
                    : [player.Team];

                if (Config.Additional.ShowSkinImage && !string.IsNullOrEmpty(image))
                {
                    _playerWeaponImage[player.Slot] = image;
                    AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
                }

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                foreach (var team in teamsToCheck)
                    playerMusic[team] = (ushort)paint;

                GivePlayerMusicKit(player);

                var selectedName = paint == 0 ? Localizer["None"] : optionMap.FirstOrDefault(x => x.Value.id == paint).Value.ToString();
                if (!string.IsNullOrEmpty(Localizer["wp_music_menu_select"]))
                    player.Print(Localizer["wp_music_menu_select", selectedName ?? "Unknown"]);

                if (WeaponSync != null)
                    _ = Task.Run(async () => await WeaponSync.SyncMusicToDatabase(playerInfo, (ushort)paint, teamsToCheck));
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    private void OpenPinsMenu(CCSPlayerController player)
    {
        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, (int id, string image)>();
        int i = 0;

        // Add "None" option
        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["None"])]));
        optionMap[i++] = (0, "");

        foreach (var pinObject in PinsList)
        {
            var paintName = pinObject["name"]?.ToString() ?? "";
            if (paintName.Length == 0) continue;

            if (!pinObject.ContainsKey("id")) continue;
            if (!int.TryParse(pinObject["id"]?.ToString(), out var id)) continue;

            var image = pinObject["image"]?.ToString() ?? "";
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(paintName)]));
            optionMap[i++] = (id, image);
        }

        if (items.Count == 0) return;

        Menu.ShowScrollableMenu(
            player,
            Localizer["wp_pins_menu_title"],
            items,
            (buttons, menu, selected) =>
            {
                if (selected == null || buttons != MenuButtons.Select) return;
                if (!optionMap.TryGetValue(menu.Option, out var pinData)) return;
                if (!Utility.IsPlayerValid(player)) return;

                // Check menu selection cooldown to prevent spam/dup
                if (MenuSelectionCooldown.TryGetValue(player.Slot, out var selCooldown) && DateTime.UtcNow < selCooldown)
                {
                    if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
                        player.Print(Localizer["wp_command_cooldown"]);
                    return;
                }
                MenuSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

                var (paint, image) = pinData;
                var playerPins = GPlayersPin.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ushort>());
                var teamsToCheck = player.TeamNum < 2
                    ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist }
                    : [player.Team];

                if (Config.Additional.ShowSkinImage && !string.IsNullOrEmpty(image))
                {
                    _playerWeaponImage[player.Slot] = image;
                    AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
                }

                PlayerInfo playerInfo = new()
                {
                    UserId = player.UserId,
                    Slot = player.Slot,
                    Index = (int)player.Index,
                    SteamId = player.SteamID.ToString(),
                    Name = player.PlayerName,
                    IpAddress = player.IpAddress?.Split(":")[0]
                };

                foreach (var team in teamsToCheck)
                    playerPins[team] = (ushort)paint;

                GivePlayerPin(player);

                var selectedName = paint == 0 ? Localizer["None"] : optionMap.FirstOrDefault(x => x.Value.id == paint).Value.ToString();
                if (!string.IsNullOrEmpty(Localizer["wp_pins_menu_select"]))
                    player.Print(Localizer["wp_pins_menu_select", selectedName ?? "Unknown"]);

                if (WeaponSync != null)
                    _ = Task.Run(async () => await WeaponSync.SyncPinToDatabase(playerInfo, (ushort)paint, teamsToCheck));
            },
            false, Config.Additional.MenuFreezePlayer);
    }

    #endregion

    private void OnCommandStattrak(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid) return;

        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

        if (weapon == null || !weapon.IsValid)
            return;

        if (!HasChangedPaint(player, weapon.AttributeManager.Item.ItemDefinitionIndex, out var weaponInfo) || weaponInfo == null)
            return;

        weaponInfo.StatTrak = !weaponInfo.StatTrak;
        RefreshWeapons(player);

        if (!string.IsNullOrEmpty(Localizer["wp_stattrak_action"]))
        {
            player.Print(Localizer["wp_stattrak_action"]);
        }
    }
}
