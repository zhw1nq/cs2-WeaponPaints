using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using Menu;
using Menu.Enums;

namespace WeaponPaints;

public partial class WeaponPaints
{
	private KitsuneMenu? _menuManager;

	private void InitializeMenuManager()
	{
		_menuManager = new KitsuneMenu(this, multiCast: false);
	}

	private void OnCommandRefresh(CCSPlayerController? player, CommandInfo command)
	{
		if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled || !_gBCommandsAllowed || !Utility.IsPlayerValid(player) || player?.UserId == null) return;

		PlayerInfo? playerInfo = new()
		{
			UserId = player.UserId,
			Slot = player.Slot,
			Index = (int)player.Index,
			SteamId = player.SteamID.ToString(),
			Name = player.PlayerName,
			IpAddress = player.IpAddress?.Split(":")[0]
		};

		try
		{
			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				if (WeaponSync != null)
				{
					_ = Task.Run(async () => await WeaponSync.GetPlayerData(playerInfo));
					GivePlayerGloves(player);
					RefreshWeapons(player);
					GivePlayerAgent(player);
					GivePlayerMusicKit(player);
					AddTimer(0.15f, () => GivePlayerPin(player));
				}

				if (!string.IsNullOrEmpty(Localizer["wp_command_refresh_done"]))
					player.Print(Localizer["wp_command_refresh_done"]);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}
		catch { }
	}

	private void OnCommandWS(CCSPlayerController? player, CommandInfo command)
	{
		if (!Config.Additional.SkinEnabled || !Utility.IsPlayerValid(player)) return;

		if (!string.IsNullOrEmpty(Localizer["wp_info_website"]))
			player!.Print(Localizer["wp_info_website", Config.Website]);

		if (!string.IsNullOrEmpty(Localizer["wp_info_refresh"]))
			player!.Print(Localizer["wp_info_refresh"]);

		if (Config.Additional.GloveEnabled && !string.IsNullOrEmpty(Localizer["wp_info_glove"]))
			player!.Print(Localizer["wp_info_glove"]);

		if (Config.Additional.AgentEnabled && !string.IsNullOrEmpty(Localizer["wp_info_agent"]))
			player!.Print(Localizer["wp_info_agent"]);

		if (Config.Additional.MusicEnabled && !string.IsNullOrEmpty(Localizer["wp_info_music"]))
			player!.Print(Localizer["wp_info_music"]);

		if (Config.Additional.PinsEnabled && !string.IsNullOrEmpty(Localizer["wp_info_pin"]))
			player!.Print(Localizer["wp_info_pin"]);

		if (Config.Additional.KnifeEnabled && !string.IsNullOrEmpty(Localizer["wp_info_knife"]))
			player!.Print(Localizer["wp_info_knife"]);
	}

	private void RegisterCommands()
	{
		_config.Additional.CommandStattrak.ForEach(c => AddCommand($"css_{c}", "Stattrak toggle", (player, info) =>
		{
			if (Utility.IsPlayerValid(player)) OnCommandStattrak(player, info);
		}));

		_config.Additional.CommandSkin.ForEach(c => AddCommand($"css_{c}", "Skins info", (player, info) =>
		{
			if (Utility.IsPlayerValid(player)) OnCommandWS(player, info);
		}));

		_config.Additional.CommandRefresh.ForEach(c => AddCommand($"css_{c}", "Skins refresh", (player, info) =>
		{
			if (Utility.IsPlayerValid(player)) OnCommandRefresh(player, info);
		}));

		if (Config.Additional.CommandKillEnabled)
		{
			_config.Additional.CommandKill.ForEach(c => AddCommand($"css_{c}", "kill yourself", (player, _) =>
			{
				if (player?.PlayerPawn.Value != null && player.PlayerPawn.IsValid)
					player.PlayerPawn.Value.CommitSuicide(true, false);
			}));
		}
	}

	private void OnCommandStattrak(CCSPlayerController? player, CommandInfo commandInfo)
	{
		if (player?.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is not { } weapon) return;

		if (!HasChangedPaint(player, weapon.AttributeManager.Item.ItemDefinitionIndex, out var weaponInfo) || weaponInfo == null) return;

		weaponInfo.StatTrak = !weaponInfo.StatTrak;
		RefreshWeapons(player);

		if (!string.IsNullOrEmpty(Localizer["wp_stattrak_action"]))
			player.Print(Localizer["wp_stattrak_action"]);
	}

	private void SetupKnifeMenu()
	{
		if (!Config.Additional.KnifeEnabled || !_gBCommandsAllowed) return;

		var knivesOnly = WeaponList
			.Where(pair => pair.Key.StartsWith("weapon_knife") || pair.Key.StartsWith("weapon_bayonet"))
			.ToDictionary(pair => pair.Key, pair => pair.Value);

		_config.Additional.CommandKnife.ForEach(c => AddCommand($"css_{c}", "Knife Menu", (player, _) =>
		{
			if (!Utility.IsPlayerValid(player) || !_gBCommandsAllowed || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var items = knivesOnly.Select(knifePair => new MenuItem(
					MenuItemType.Button,
					new MenuValue(string.Empty),
					[
						new MenuButtonCallback(knifePair.Value, knifePair.Key, (ctrl, data) =>
					{
						// Kiểm tra cooldown cho việc chọn knife
						if (SkinSelectionCooldown.TryGetValue(ctrl.Slot, out var selectionCooldownEndTime) && DateTime.UtcNow < selectionCooldownEndTime)
						{
							var remainingSeconds = (int)(selectionCooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
							if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
								ctrl.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
							else
								ctrl.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn knife tiếp theo!");
							return;
						}

						// Đặt cooldown từ config
						SkinSelectionCooldown[ctrl.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

						var playerKnives = GPlayersKnife.GetOrAdd(ctrl.Slot, new ConcurrentDictionary<CsTeam, string>());
						var teamsToCheck = ctrl.TeamNum < 2 ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist } : new[] { ctrl.Team };

						if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_select"]))
							ctrl.PrintToChat(Localizer["wp_knife_menu_select", knifePair.Value]);

						if (!string.IsNullOrEmpty(Localizer["wp_knife_menu_kill"]) && Config.Additional.CommandKillEnabled)
							ctrl.PrintToChat(Localizer["wp_knife_menu_kill"]);

						PlayerInfo playerInfo = new()
						{
							UserId = ctrl.UserId,
							Slot = ctrl.Slot,
							Index = (int)ctrl.Index,
							SteamId = ctrl.SteamID.ToString(),
							Name = ctrl.PlayerName,
							IpAddress = ctrl.IpAddress?.Split(":")[0]
						};

						foreach (var team in teamsToCheck)
							playerKnives[team] = data;

						if (_gBCommandsAllowed && (LifeState_t)ctrl.LifeState == LifeState_t.LIFE_ALIVE)
							RefreshWeapons(ctrl);

						if (WeaponSync != null)
							Task.Run(async () => await WeaponSync.SyncKnifeToDatabase(playerInfo, data, teamsToCheck));
					})
					]
				)).ToList();

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_knife_menu_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void SetupSkinsMenu()
	{
		var classNamesByWeapon = WeaponList
			.Except([new KeyValuePair<string, string>("weapon_knife", "Default Knife")])
			.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

		_config.Additional.CommandSkinSelection.ForEach(c => AddCommand($"css_{c}", "Skins selection menu", (player, _) =>
		{
			if (!Utility.IsPlayerValid(player) || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var items = classNamesByWeapon.Select(weaponPair => new MenuItem(
					MenuItemType.Button,
					new MenuValue(string.Empty),
					[
						new MenuButtonCallback(weaponPair.Key, weaponPair.Value, (ctrl, weaponClassName) =>
						{
							ShowSkinsForWeapon(ctrl, weaponPair.Key, weaponClassName);
						})
					]
				)).ToList();

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_skin_menu_weapon_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void ShowSkinsForWeapon(CCSPlayerController player, string weaponName, string weaponClassName)
	{
		var skinsForSelectedWeapon = SkinsList.Where(skin =>
			skin.TryGetValue("weapon_name", out var wName) && wName?.ToString() == weaponClassName
		)?.ToList();

		if (skinsForSelectedWeapon == null || !skinsForSelectedWeapon.Any()) return;

		var items = skinsForSelectedWeapon
			.Where(skin => skin.TryGetValue("paint_name", out var paintNameObj) && skin.TryGetValue("paint", out var paintObj) &&
				!string.IsNullOrEmpty(paintNameObj?.ToString()) && !string.IsNullOrEmpty(paintObj?.ToString()))
			.Select(skin =>
			{
				var paintName = skin["paint_name"]!.ToString();
				var paint = skin["paint"]!.ToString();

				return new MenuItem(
					MenuItemType.Button,
					new MenuValue(string.Empty),
					[
						new MenuButtonCallback($"{paintName} ({paint})", $"{weaponClassName}|{paint}", (ctrl, data) =>
						{
							// Kiểm tra cooldown
							if (SkinSelectionCooldown.TryGetValue(ctrl.Slot, out var cooldownEndTime) && DateTime.UtcNow < cooldownEndTime)
							{
								var remainingSeconds = (int)(cooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
								if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
									ctrl.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
								else
									ctrl.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn skin tiếp theo!");
								return;
							}

							var parts = data.Split('|');
							if (parts.Length != 2 || !int.TryParse(parts[1], out var paintId)) return;

							var weaponClass = parts[0];
							var firstSkin = SkinsList.FirstOrDefault(s =>
								s.TryGetValue("weapon_name", out var wn) && wn?.ToString() == weaponClass);

							if (firstSkin == null || !firstSkin.TryGetValue("weapon_defindex", out var weaponDefIndexObj) ||
								!int.TryParse(weaponDefIndexObj.ToString(), out var weaponDefIndex)) return;

							// Đặt cooldown từ config
							SkinSelectionCooldown[ctrl.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

							if (Config.Additional.ShowSkinImage)
							{
								var foundSkin = SkinsList.FirstOrDefault(s =>
									((int?)s["weapon_defindex"] ?? 0) == weaponDefIndex &&
									((int?)s["paint"] ?? 0) == paintId &&
									s["image"] != null
								);
								var image = foundSkin?["image"]?.ToString() ?? "";
								_playerWeaponImage[ctrl.Slot] = image;
								AddTimer(2.0f, () => _playerWeaponImage.Remove(ctrl.Slot), TimerFlags.STOP_ON_MAPCHANGE);
							}

							ctrl.PrintToChat(Localizer["wp_skin_menu_select", $"{paintName} ({paint})"]);

							var playerSkins = GPlayerWeaponsInfo.GetOrAdd(ctrl.Slot, new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());
							var teamsToCheck = ctrl.TeamNum < 2 ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist } : new[] { ctrl.Team };

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
								UserId = ctrl.UserId,
								Slot = ctrl.Slot,
								Index = (int)ctrl.Index,
								SteamId = ctrl.SteamID.ToString(),
								Name = ctrl.PlayerName,
								IpAddress = ctrl.IpAddress?.Split(":")[0]
							};

							if (_gBCommandsAllowed && (LifeState_t)ctrl.LifeState == LifeState_t.LIFE_ALIVE)
								RefreshWeapons(ctrl);

							if (WeaponSync != null)
								_ = Task.Run(async () => await WeaponSync.SyncWeaponPaintsToDatabase(playerInfo));
						})
					]
				);
			}).ToList();

		_menuManager!.ShowScrollableMenu(player, Localizer["wp_skin_menu_skin_title", weaponName], items, null, false, true, 5);
	}

	private void SetupGlovesMenu()
	{
		_config.Additional.CommandGlove.ForEach(c => AddCommand($"css_{c}", "Gloves selection menu", (player, info) =>
		{
			if (!Utility.IsPlayerValid(player) || !_gBCommandsAllowed || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var items = GlovesList
					.Where(glove => !string.IsNullOrEmpty(glove["paint_name"]?.ToString()))
					.Select(glove =>
					{
						var paintName = glove["paint_name"]!.ToString();
						return new MenuItem(
							MenuItemType.Button,
							new MenuValue(string.Empty),
							[
								new MenuButtonCallback(paintName, paintName, (ctrl, data) =>
							{
								// Kiểm tra cooldown cho việc chọn glove
								if (SkinSelectionCooldown.TryGetValue(ctrl.Slot, out var selectionCooldownEndTime) && DateTime.UtcNow < selectionCooldownEndTime)
								{
									var remainingSeconds = (int)(selectionCooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
									if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
										ctrl.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
									else
										ctrl.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn glove tiếp theo!");
									return;
								}

								// Đặt cooldown từ config
								SkinSelectionCooldown[ctrl.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

								var selectedGlove = GlovesList.FirstOrDefault(g =>
									g.ContainsKey("paint_name") && g["paint_name"]?.ToString() == data);

								if (selectedGlove == null || !selectedGlove.ContainsKey("weapon_defindex") ||
									!selectedGlove.ContainsKey("paint") ||
									!int.TryParse(selectedGlove["weapon_defindex"]?.ToString(), out var weaponDefindex) ||
									!int.TryParse(selectedGlove["paint"]?.ToString(), out var paint)) return;

								if (Config.Additional.ShowSkinImage)
								{
									var image = selectedGlove["image"]?.ToString() ?? "";
									_playerWeaponImage[ctrl.Slot] = image;
									AddTimer(2.0f, () => _playerWeaponImage.Remove(ctrl.Slot), TimerFlags.STOP_ON_MAPCHANGE);
								}

								var playerGloves = GPlayersGlove.GetOrAdd(ctrl.Slot, new ConcurrentDictionary<CsTeam, ushort>());
								var teamsToCheck = ctrl.TeamNum < 2 ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist } : new[] { ctrl.Team };

								PlayerInfo playerInfo = new()
								{
									UserId = ctrl.UserId,
									Slot = ctrl.Slot,
									Index = (int)ctrl.Index,
									SteamId = ctrl.SteamID.ToString(),
									Name = ctrl.PlayerName,
									IpAddress = ctrl.IpAddress?.Split(":")[0]
								};

								if (paint != 0)
								{
									GPlayerWeaponsInfo.TryAdd(ctrl.Slot, new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());

									foreach (var team in teamsToCheck)
									{
										GPlayerWeaponsInfo[ctrl.Slot].TryAdd(team, new ConcurrentDictionary<int, WeaponInfo>());
										playerGloves[team] = (ushort)weaponDefindex;

										if (!GPlayerWeaponsInfo[ctrl.Slot][team].ContainsKey(weaponDefindex))
										{
											GPlayerWeaponsInfo[ctrl.Slot][team][weaponDefindex] = new WeaponInfo { Paint = paint };
										}
									}
								}
								else
								{
									GPlayersGlove.TryRemove(ctrl.Slot, out _);
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

								AddTimer(0.1f, () => GivePlayerGloves(ctrl));
								AddTimer(0.25f, () => GivePlayerGloves(ctrl));
							})
							]
						);
					}).ToList();

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_glove_menu_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void SetupAgentsMenu()
	{
		_config.Additional.CommandAgent.ForEach(c => AddCommand($"css_{c}", "Agents selection menu", (player, info) =>
		{
			if (!Utility.IsPlayerValid(player) || !_gBCommandsAllowed || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out DateTime cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var filteredAgents = AgentsList.Where(agentObject => agentObject["team"]?.Value<int>() == player.TeamNum).ToList();

				var items = filteredAgents
					.Where(agentObject => !string.IsNullOrEmpty(agentObject["agent_name"]?.ToString()))
					.Select(agentObject =>
					{
						var agentName = agentObject["agent_name"]!.ToString();
						return new MenuItem(
							MenuItemType.Button,
							new MenuValue(string.Empty),
							[
								new MenuButtonCallback(agentName, agentName, (ctrl, data) =>
							{
								// Kiểm tra cooldown cho việc chọn agent
								if (SkinSelectionCooldown.TryGetValue(ctrl.Slot, out var selectionCooldownEndTime) && DateTime.UtcNow < selectionCooldownEndTime)
								{
									var remainingSeconds = (int)(selectionCooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
									if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
										ctrl.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
									else
										ctrl.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn agent tiếp theo!");
									return;
								}

								// Đặt cooldown từ config
								SkinSelectionCooldown[ctrl.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

								var selectedAgent = AgentsList.FirstOrDefault(g =>
									g.ContainsKey("agent_name") &&
									g["agent_name"]?.ToString() == data &&
									g["team"] != null && (int)(g["team"]!) == ctrl.TeamNum);

								if (selectedAgent == null || !selectedAgent.ContainsKey("model")) return;

								if (Config.Additional.ShowSkinImage)
								{
									var image = selectedAgent["image"]?.ToString() ?? "";
									_playerWeaponImage[ctrl.Slot] = image;
									AddTimer(2.0f, () => _playerWeaponImage.Remove(ctrl.Slot), TimerFlags.STOP_ON_MAPCHANGE);
								}

								if (!string.IsNullOrEmpty(Localizer["wp_agent_menu_select"]))
									ctrl.PrintToChat(Localizer["wp_agent_menu_select", data]);

								PlayerInfo playerInfo = new()
								{
									UserId = ctrl.UserId,
									Slot = ctrl.Slot,
									Index = (int)ctrl.Index,
									SteamId = ctrl.SteamID.ToString(),
									Name = ctrl.PlayerName,
									IpAddress = ctrl.IpAddress?.Split(":")[0]
								};

								if (ctrl.TeamNum == 3)
								{
									GPlayersAgent.AddOrUpdate(ctrl.Slot,
										key => (selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString(), null),
										(key, oldValue) => (selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString(), oldValue.T));
								}
								else
								{
									GPlayersAgent.AddOrUpdate(ctrl.Slot,
										key => (null, selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString()),
										(key, oldValue) => (oldValue.CT, selectedAgent["model"]!.ToString().Equals("null") ? null : selectedAgent["model"]!.ToString())
									);
								}

								if (WeaponSync != null)
									_ = Task.Run(async () => await WeaponSync.SyncAgentToDatabase(playerInfo));
							})
							]
						);
					}).ToList();

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_agent_menu_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void SetupMusicMenu()
	{
		_config.Additional.CommandMusic.ForEach(c => AddCommand($"css_{c}", "Music selection menu", (player, info) =>
		{
			if (!Utility.IsPlayerValid(player) || !_gBCommandsAllowed || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var items = new List<MenuItem>
				{
				new(MenuItemType.Button, new MenuValue(string.Empty),
				[
					new MenuButtonCallback(Localizer["None"], "0", (ctrl, data) => HandleMusicSelection(ctrl, null))
				])
				};

				items.AddRange(MusicList
					.Where(musicObject => !string.IsNullOrEmpty(musicObject["name"]?.ToString()))
					.Select(musicObject =>
					{
						var musicName = musicObject["name"]!.ToString();
						return new MenuItem(
							MenuItemType.Button,
							new MenuValue(string.Empty),
							[
								new MenuButtonCallback(musicName, musicName, (ctrl, data) => HandleMusicSelection(ctrl, data))
							]
						);
					}));

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_music_menu_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void HandleMusicSelection(CCSPlayerController player, string? selectedMusicName)
	{
		// Kiểm tra cooldown cho việc chọn music
		if (SkinSelectionCooldown.TryGetValue(player.Slot, out var selectionCooldownEndTime) && DateTime.UtcNow < selectionCooldownEndTime)
		{
			var remainingSeconds = (int)(selectionCooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
			if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
				player.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
			else
				player.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn music tiếp theo!");
			return;
		}

		// Đặt cooldown từ config
		SkinSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

		var playerMusic = GPlayersMusic.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ushort>());
		var teamsToCheck = player.TeamNum < 2 ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist } : new[] { player.Team };

		PlayerInfo playerInfo = new()
		{
			UserId = player.UserId,
			Slot = player.Slot,
			Index = (int)player.Index,
			SteamId = player.SteamID.ToString(),
			Name = player.PlayerName,
			IpAddress = player.IpAddress?.Split(":")[0]
		};

		if (selectedMusicName != null)
		{
			var selectedMusic = MusicList.FirstOrDefault(g => g.ContainsKey("name") && g["name"]?.ToString() == selectedMusicName);
			if (selectedMusic != null && selectedMusic.ContainsKey("id") && int.TryParse(selectedMusic["id"]?.ToString(), out var paint))
			{
				if (Config.Additional.ShowSkinImage)
				{
					var image = selectedMusic["image"]?.ToString() ?? "";
					_playerWeaponImage[player.Slot] = image;
					AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
				}

				foreach (var team in teamsToCheck)
					playerMusic[team] = (ushort)paint;

				GivePlayerMusicKit(player);

				if (!string.IsNullOrEmpty(Localizer["wp_music_menu_select"]))
					player.PrintToChat(Localizer["wp_music_menu_select", selectedMusicName]);

				if (WeaponSync != null)
					_ = Task.Run(async () => await WeaponSync.SyncMusicToDatabase(playerInfo, (ushort)paint, teamsToCheck));
				return;
			}
		}

		foreach (var team in teamsToCheck)
			playerMusic[team] = 0;

		GivePlayerMusicKit(player);

		if (!string.IsNullOrEmpty(Localizer["wp_music_menu_select"]))
			player.PrintToChat(Localizer["wp_music_menu_select", Localizer["None"]]);

		if (WeaponSync != null)
			_ = Task.Run(async () => await WeaponSync.SyncMusicToDatabase(playerInfo, 0, teamsToCheck));
	}

	private void SetupPinsMenu()
	{
		_config.Additional.CommandPin.ForEach(c => AddCommand($"css_{c}", "Pin selection menu", (player, info) =>
		{
			if (!Utility.IsPlayerValid(player) || !_gBCommandsAllowed || player?.UserId == null) return;

			if (!CommandsCooldown.TryGetValue(player.Slot, out var cooldownEndTime) || DateTime.UtcNow >= cooldownEndTime)
			{
				CommandsCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.CmdRefreshCooldownSeconds);

				var items = new List<MenuItem>
				{
				new(MenuItemType.Button, new MenuValue(string.Empty),
				[
					new MenuButtonCallback(Localizer["None"], "0", (ctrl, data) => HandlePinSelection(ctrl, null))
				])
				};

				items.AddRange(PinsList
					.Where(pinObject => !string.IsNullOrEmpty(pinObject["name"]?.ToString()))
					.Select(pinObject =>
					{
						var pinName = pinObject["name"]!.ToString();
						return new MenuItem(
							MenuItemType.Button,
							new MenuValue(string.Empty),
							[
								new MenuButtonCallback(pinName, pinName, (ctrl, data) => HandlePinSelection(ctrl, data))
							]
						);
					}));

				_menuManager!.ShowScrollableMenu(player, Localizer["wp_pins_menu_title"], items, null, false, true, 5);
				return;
			}

			if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"]))
				player.Print(Localizer["wp_command_cooldown"]);
		}));
	}

	private void HandlePinSelection(CCSPlayerController player, string? selectedPinName)
	{
		// Kiểm tra cooldown cho việc chọn pin
		if (SkinSelectionCooldown.TryGetValue(player.Slot, out var selectionCooldownEndTime) && DateTime.UtcNow < selectionCooldownEndTime)
		{
			var remainingSeconds = (int)(selectionCooldownEndTime - DateTime.UtcNow).TotalSeconds + 1;
			if (!string.IsNullOrEmpty(Localizer["wp_skin_selection_cooldown"]))
				player.PrintToChat(Localizer["wp_skin_selection_cooldown", remainingSeconds]);
			else
				player.PrintToChat($" {ChatColors.Red}Vui lòng đợi {remainingSeconds} giây trước khi chọn pin tiếp theo!");
			return;
		}

		// Đặt cooldown từ config
		SkinSelectionCooldown[player.Slot] = DateTime.UtcNow.AddSeconds(Config.SkinSelectionCooldownSeconds);

		var playerPins = GPlayersPin.GetOrAdd(player.Slot, new ConcurrentDictionary<CsTeam, ushort>());
		var teamsToCheck = player.TeamNum < 2 ? new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist } : new[] { player.Team };

		PlayerInfo playerInfo = new()
		{
			UserId = player.UserId,
			Slot = player.Slot,
			Index = (int)player.Index,
			SteamId = player.SteamID.ToString(),
			Name = player.PlayerName,
			IpAddress = player.IpAddress?.Split(":")[0]
		};

		if (selectedPinName != null)
		{
			var selectedPin = PinsList.FirstOrDefault(g => g.ContainsKey("name") && g["name"]?.ToString() == selectedPinName);
			if (selectedPin != null && selectedPin.ContainsKey("id") && int.TryParse(selectedPin["id"]?.ToString(), out var paint))
			{
				if (Config.Additional.ShowSkinImage)
				{
					var image = selectedPin["image"]?.ToString() ?? "";
					_playerWeaponImage[player.Slot] = image;
					AddTimer(2.0f, () => _playerWeaponImage.Remove(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
				}

				foreach (var team in teamsToCheck)
					playerPins[team] = (ushort)paint;

				if (!string.IsNullOrEmpty(Localizer["wp_pins_menu_select"]))
					player.PrintToChat(Localizer["wp_pins_menu_select", selectedPinName]);

				GivePlayerPin(player);

				if (WeaponSync != null)
					_ = Task.Run(async () => await WeaponSync.SyncPinToDatabase(playerInfo, (ushort)paint, teamsToCheck));
				return;
			}
		}

		foreach (var team in teamsToCheck)
			playerPins[team] = 0;

		if (!string.IsNullOrEmpty(Localizer["wp_pins_menu_select"]))
			player.PrintToChat(Localizer["wp_pins_menu_select", Localizer["None"]]);

		GivePlayerPin(player);

		if (WeaponSync != null)
			_ = Task.Run(async () => await WeaponSync.SyncPinToDatabase(playerInfo, 0, teamsToCheck));
	}
}