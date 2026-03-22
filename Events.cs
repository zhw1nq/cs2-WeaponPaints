using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace WeaponPaints
{
    public partial class WeaponPaints
    {
        private bool _mvpPlayed;

        [GameEventHandler]
        public HookResult OnClientFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;

            if (player is null || !player.IsValid || player.IsBot ||
                WeaponSync == null || Database == null) return HookResult.Continue;

            var playerInfo = new PlayerInfo
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
                // Call sync method - it handles ThreadPool internally to avoid Harmony patching
                WeaponSync.GetPlayerData(playerInfo);
                /*
				if (Config.Additional.SkinEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetWeaponPaintsFromDatabase(playerInfo));
				}
				if (Config.Additional.KnifeEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetKnifeFromDatabase(playerInfo));
				}
				if (Config.Additional.GloveEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetGloveFromDatabase(playerInfo));
				}
				if (Config.Additional.AgentEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetAgentFromDatabase(playerInfo));
				}
				if (Config.Additional.MusicEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetMusicFromDatabase(playerInfo));
				}
				*/
            }
            catch
            {
            }

            Players.Add(player);

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;

            if (player is null || !player.IsValid || player.IsBot) return HookResult.Continue;

            var playerInfo = new PlayerInfo
            {
                UserId = player.UserId,
                Slot = player.Slot,
                Index = (int)player.Index,
                SteamId = player.SteamID.ToString(),
                Name = player.PlayerName,
                IpAddress = player.IpAddress?.Split(":")[0]
            };

            // Call sync method - it handles ThreadPool internally to avoid Harmony async patching issues
            try
            {
                if (WeaponSync != null)
                    WeaponSync.SyncStatTrakToDatabase(playerInfo);

                if (Config.Additional.SkinEnabled)
                {
                    GPlayerWeaponsInfo.TryRemove(player.Slot, out _);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[OnPlayerDisconnect] Error: {ex.GetType().Name}: {ex.Message}");
            }

            if (Config.Additional.KnifeEnabled)
            {
                GPlayersKnife.TryRemove(player.Slot, out _);
            }
            if (Config.Additional.GloveEnabled)
            {
                GPlayersGlove.TryRemove(player.Slot, out _);
            }
            if (Config.Additional.AgentEnabled)
            {
                GPlayersAgent.TryRemove(player.Slot, out _);
            }
            if (Config.Additional.MusicEnabled)
            {
                GPlayersMusic.TryRemove(player.Slot, out _);
            }
            if (Config.Additional.PinsEnabled)
            {
                GPlayersPin.TryRemove(player.Slot, out _);
            }

            _temporaryPlayerWeaponWear.TryRemove(player.Slot, out _);
            CommandsCooldown.Remove(player.Slot);
            MenuSelectionCooldown.Remove(player.Slot);
            Players.Remove(player);

            return HookResult.Continue;
        }

        private void OnMapStart(string mapName)
        {
            if (Config.Additional is { KnifeEnabled: false, SkinEnabled: false, GloveEnabled: false }) return;

            if (Database != null)
                WeaponSync = new WeaponSynchronization(Database, Config);

            _fadeSeed = 0;
            _nextItemId = MinimumCustomItemId;
        }

        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;

            if (player is null || !player.IsValid || Config.Additional is { KnifeEnabled: false, GloveEnabled: false })
                return HookResult.Continue;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;

            if (pawn == null || !pawn.IsValid)
                return HookResult.Continue;

            GivePlayerMusicKit(player);
            GivePlayerAgent(player);
            Server.NextFrame(() =>
            {
                GivePlayerGloves(player);
            });
            GivePlayerPin(player);

            return HookResult.Continue;
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            _gBCommandsAllowed = false;
            return HookResult.Continue;
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            _gBCommandsAllowed = true;
            _mvpPlayed = false;
            return HookResult.Continue;
        }

        private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
        {
            if (_mvpPlayed)
                return HookResult.Continue;

            var player = @event.Userid;

            if (player == null || !player.IsValid || player.IsBot)
                return HookResult.Continue;

            if (!(GPlayersMusic.TryGetValue(player.Slot, out var musicInfo)
                  && musicInfo.TryGetValue(player.Team, out var musicId)
                  && musicId != 0))
                return HookResult.Continue;

            @event.Musickitid = musicId;
            @event.Nomusic = 0;
            info.DontBroadcast = true;

            var newEvent = new EventRoundMvp(true)
            {
                Userid = player,
                Musickitid = musicId,
                Nomusic = 0,
            };

            _mvpPlayed = true;

            newEvent.FireEvent(false);
            return HookResult.Continue;
        }

        private HookResult OnGiveNamedItemPost(DynamicHook hook)
        {
            Logger.LogDebug($"[OnGiveNamedItemPost] ENTER");

            try
            {
                var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
                var weapon = hook.GetReturn<CBasePlayerWeapon>();

                Logger.LogDebug($"[OnGiveNamedItemPost] weapon.DesignerName={weapon?.DesignerName ?? "null"}, weapon.IsValid={weapon?.IsValid}");

                if (weapon == null || !weapon.IsValid)
                {
                    Logger.LogDebug("[OnGiveNamedItemPost] EXIT: weapon null or invalid");
                    return HookResult.Continue;
                }

                if (!weapon.DesignerName.Contains("weapon"))
                {
                    Logger.LogDebug("[OnGiveNamedItemPost] EXIT: not a weapon");
                    return HookResult.Continue;
                }

                var player = GetPlayerFromItemServices(itemServices);
                Logger.LogDebug($"[OnGiveNamedItemPost] player={player?.PlayerName ?? "null"}, SteamID={player?.SteamID}");

                if (player != null)
                {
                    Logger.LogDebug($"[OnGiveNamedItemPost] Calling GivePlayerWeaponSkin for {player.PlayerName}, weapon={weapon.DesignerName}");
                    GivePlayerWeaponSkin(player, weapon);
                }
                else
                {
                    Logger.LogDebug("[OnGiveNamedItemPost] player is null, skipping");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[OnGiveNamedItemPost] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }

            Logger.LogDebug("[OnGiveNamedItemPost] EXIT");
            return HookResult.Continue;
        }

        private void OnEntityCreated(CEntityInstance entity)
        {
            var designerName = entity.DesignerName;

            if (designerName.Contains("weapon"))
            {
                var entityIndex = entity.Index;
                var entityHandle = entity.Handle;
                Logger.LogDebug($"[OnEntityCreated] Weapon detected: {designerName}, Index={entityIndex}, Handle={entityHandle}");

                Server.NextWorldUpdate(() =>
                {
                    Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] ENTER: designerName={designerName}, entityIndex={entityIndex}");

                    CBasePlayerWeapon? weapon = null;
                    try
                    {
                        weapon = new CBasePlayerWeapon(entityHandle);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[OnEntityCreated.NextWorldUpdate] Failed to create CBasePlayerWeapon: {ex.Message}");
                        return;
                    }

                    if (weapon == null)
                    {
                        Logger.LogDebug("[OnEntityCreated.NextWorldUpdate] EXIT: weapon is null");
                        return;
                    }

                    if (!weapon.IsValid)
                    {
                        Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] EXIT: weapon.IsValid=false for {designerName}");
                        return;
                    }

                    Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] weapon.IsValid=true, DesignerName={weapon.DesignerName}, OriginalOwnerXuidLow={weapon.OriginalOwnerXuidLow}");

                    try
                    {
                        SteamID? steamid = null;

                        if (weapon.OriginalOwnerXuidLow > 0)
                        {
                            steamid = new SteamID(weapon.OriginalOwnerXuidLow);
                            Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] SteamID created: {steamid?.SteamId64}, IsValid={steamid?.IsValid()}");
                        }

                        CCSPlayerController? player = null;

                        if (steamid != null && steamid.IsValid())
                        {
                            player = Players.FirstOrDefault(p => p.IsValid && p.SteamID == steamid.SteamId64);
                            Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] Player lookup by SteamID: found={player != null}");

                            if (player == null)
                            {
                                player = Utilities.GetPlayerFromSteamId(weapon.OriginalOwnerXuidLow);
                                Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] Fallback GetPlayerFromSteamId: found={player != null}");
                            }
                        }
                        else
                        {
                            Logger.LogDebug("[OnEntityCreated.NextWorldUpdate] No valid SteamID, trying OwnerEntity lookup");
                            try
                            {
                                CCSWeaponBaseGun gun = weapon.As<CCSWeaponBaseGun>();
                                player = Utilities.GetPlayerFromIndex((int)weapon.OwnerEntity.Index);
                                if (player == null && gun.OwnerEntity.Value != null)
                                {
                                    player = Utilities.GetPlayerFromIndex((int)gun.OwnerEntity.Value.Index);
                                }
                                Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] OwnerEntity lookup: found={player != null}");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] OwnerEntity lookup failed: {ex.Message}");
                            }
                        }

                        if (string.IsNullOrEmpty(player?.PlayerName))
                        {
                            Logger.LogDebug("[OnEntityCreated.NextWorldUpdate] EXIT: player.PlayerName is null or empty");
                            return;
                        }

                        if (!Utility.IsPlayerValid(player))
                        {
                            Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] EXIT: IsPlayerValid=false for {player.PlayerName}");
                            return;
                        }

                        // ========== GUARD: Re-validate weapon before calling GivePlayerWeaponSkin ==========
                        if (!weapon.IsValid)
                        {
                            Logger.LogDebug("[OnEntityCreated.NextWorldUpdate] EXIT: weapon became invalid before GivePlayerWeaponSkin");
                            return;
                        }

                        Logger.LogDebug($"[OnEntityCreated.NextWorldUpdate] Calling GivePlayerWeaponSkin for {player.PlayerName}, weapon={weapon.DesignerName}");
                        GivePlayerWeaponSkin(player, weapon);
                        Logger.LogDebug("[OnEntityCreated.NextWorldUpdate] GivePlayerWeaponSkin completed");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[OnEntityCreated.NextWorldUpdate] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
        }

        private void OnTick()
        {
            foreach (var player in Players)
            {
                if (!player.IsValid) continue;

                // Show skin image
                if (Config.Additional.ShowSkinImage && _playerWeaponImage.TryGetValue(player.Slot, out var value) && !string.IsNullOrEmpty(value))
                {
                    player.PrintToCenterHtml("<img src='{PATH}'</img>".Replace("{PATH}", value));
                }
                // Show disabled command message (priority over skin image)
                else if (_playerDisabledMessageEndTime.TryGetValue(player.Slot, out var endTime) && DateTime.UtcNow < endTime)
                {
                    var centerMessage = $"<font color='#FF6B6B'>zDevelopment-Vietnam</font><br>" +
                                       $"<font color='#FFFFFF'>Lệnh này đang tạm thời bị vô hiệu hóa để bảo trì.</font><br>" +
                                       $"<font color='#90EE90'>Vui lòng sử dụng Changer tại {Config.Website}</font><br>" +
                                       $"<font color='#FFFFFF'>Reconnect server để áp dụng skin mới hoặc dùng lệnh </font><font color='#90EE90'>!wp</font>";
                    player.PrintToCenterHtml(centerMessage);
                }
                else if (_playerDisabledMessageEndTime.ContainsKey(player.Slot))
                {
                    // Remove expired entry
                    _playerDisabledMessageEndTime.Remove(player.Slot);
                }
            }
        }

        [GameEventHandler]
        public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo _)
        {
            // if (!IsWindows) return HookResult.Continue;
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
            if (!@event.Item.Contains("knife")) return HookResult.Continue;

            var weaponDefIndex = (int)@event.Defindex;

            if (!HasChangedKnife(player, out var _) || !HasChangedPaint(player, weaponDefIndex, out var _))
                return HookResult.Continue;

            if (player is { Connected: PlayerConnectedState.PlayerConnected, PawnIsAlive: true, PlayerPawn.IsValid: true })
            {
                GiveOnItemPickup(player);
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Attacker;
            CCSPlayerController? victim = @event.Userid;

            if (player is null || !player.IsValid)
                return HookResult.Continue;

            if (victim == null || !victim.IsValid || victim == player)
                return HookResult.Continue;

            CBasePlayerWeapon? weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

            if (weapon == null) return HookResult.Continue;

            int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

            if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
                return HookResult.Continue;

            if (!weaponInfo.StatTrak) return HookResult.Continue;

            weaponInfo.StatTrakCount += 1;

            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);

            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);

            return HookResult.Continue;
        }

        private void RegisterListeners()
        {
            RegisterListener<Listeners.OnMapStart>(OnMapStart);

            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
            RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntityCreated);
            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

            // Always register OnTick for disabled command messages and skin images
            RegisterListener<Listeners.OnTick>(OnTick);

            VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
        }
    }
}