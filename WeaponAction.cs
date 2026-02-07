using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace WeaponPaints
{
    public partial class WeaponPaints
    {
        private void GivePlayerWeaponSkin(CCSPlayerController player, CBasePlayerWeapon weapon)
        {
            // ========== DEBUG: Entry Point ==========
            Logger.LogDebug($"[GivePlayerWeaponSkin] ENTER: player={player?.PlayerName ?? "null"}, SteamID={player?.SteamID}, weapon={weapon?.DesignerName ?? "null"}");

            try
            {
                // ========== GUARD: Validate weapon ==========
                if (weapon == null)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon is null");
                    return;
                }

                if (!weapon.IsValid)
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] EXIT: weapon.IsValid=false, DesignerName={weapon.DesignerName}");
                    return;
                }

                // ========== GUARD: Validate player ==========
                if (player == null || !player.IsValid)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: player is null or invalid");
                    return;
                }

                if (!Config.Additional.SkinEnabled)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: SkinEnabled=false");
                    return;
                }

                if (!GPlayerWeaponsInfo.TryGetValue(player.Slot, out _))
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] EXIT: No weapon info for player.Slot={player.Slot}");
                    return;
                }

                // ========== GUARD: Re-validate weapon before proceeding ==========
                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon became invalid after initial checks");
                    return;
                }

                bool isKnife = weapon.DesignerName.Contains("knife") || weapon.DesignerName.Contains("bayonet");
                Logger.LogDebug($"[GivePlayerWeaponSkin] isKnife={isKnife}, DesignerName={weapon.DesignerName}");

                switch (isKnife)
                {
                    case true when !HasChangedKnife(player, out var _):
                        Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: isKnife but HasChangedKnife=false");
                        return;

                    case true:
                        {
                            Logger.LogDebug($"[GivePlayerWeaponSkin] Processing KNIFE for player={player.PlayerName}");

                            // ========== GUARD: Check data exists ==========
                            if (!GPlayersKnife.TryGetValue(player.Slot, out var knifeDict) || !knifeDict.TryGetValue(player.Team, out var knifeValue))
                            {
                                Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: No knife data for this slot/team");
                                return;
                            }

                            var newDefIndex = WeaponDefindex.FirstOrDefault(x => x.Value == knifeValue);
                            Logger.LogDebug($"[GivePlayerWeaponSkin] Knife: knifeValue={knifeValue}, newDefIndex.Key={newDefIndex.Key}");

                            if (newDefIndex.Key == 0)
                            {
                                Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: newDefIndex.Key==0 (invalid knife)");
                                return;
                            }

                            // ========== GUARD: Re-validate weapon before SubclassChange ==========
                            if (!weapon.IsValid)
                            {
                                Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon became invalid before SubclassChange");
                                return;
                            }

                            if (weapon.AttributeManager.Item.ItemDefinitionIndex != newDefIndex.Key)
                            {
                                Logger.LogDebug($"[GivePlayerWeaponSkin] Calling SubclassChange: current={weapon.AttributeManager.Item.ItemDefinitionIndex}, new={newDefIndex.Key}");
                                SubclassChange(weapon, (ushort)newDefIndex.Key);

                                // ========== GUARD: Re-validate after SubclassChange ==========
                                if (!weapon.IsValid)
                                {
                                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon became invalid AFTER SubclassChange");
                                    return;
                                }
                            }

                            weapon.AttributeManager.Item.ItemDefinitionIndex = (ushort)newDefIndex.Key;
                            weapon.AttributeManager.Item.EntityQuality = 3;

                            weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
                            weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
                            Logger.LogDebug("[GivePlayerWeaponSkin] Knife attributes cleared");
                            break;
                        }
                    default:
                        weapon.AttributeManager.Item.EntityQuality = 0;
                        break;
                }

                // ========== GUARD: Re-validate weapon ==========
                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon became invalid after knife processing");
                    return;
                }

                UpdatePlayerEconItemId(weapon.AttributeManager.Item);

                int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
                int fallbackPaintKit;

                weapon.AttributeManager.Item.AccountID = (uint)player.SteamID;
                Logger.LogDebug($"[GivePlayerWeaponSkin] weaponDefIndex={weaponDefIndex}, AccountID set");

                List<JObject> skinInfo;
                bool isLegacyModel;

                if (_config.Additional.GiveRandomSkin &&
                    !HasChangedPaint(player, weaponDefIndex, out _))
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] Applying RANDOM skin");

                    // ========== GUARD: Re-validate weapon ==========
                    if (!weapon.IsValid)
                    {
                        Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon invalid before random skin");
                        return;
                    }

                    // Random skins
                    weapon.FallbackPaintKit = GetRandomPaint(weaponDefIndex);
                    weapon.FallbackSeed = 0;
                    weapon.FallbackWear = 0.01f;

                    weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture prefab", GetRandomPaint(weaponDefIndex));
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture seed", 0);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture wear", 0.01f);

                    weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture prefab", GetRandomPaint(weaponDefIndex));
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture seed", 0);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture wear", 0.01f);

                    fallbackPaintKit = weapon.FallbackPaintKit;
                    Logger.LogDebug($"[GivePlayerWeaponSkin] Random: fallbackPaintKit={fallbackPaintKit}");

                    if (fallbackPaintKit == 0)
                    {
                        Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: random fallbackPaintKit==0");
                        return;
                    }

                    skinInfo = SkinsList
                        .Where(w =>
                            w["weapon_defindex"]?.ToObject<int>() == weaponDefIndex &&
                            w["paint"]?.ToObject<int>() == fallbackPaintKit)
                        .ToList();

                    Logger.LogDebug($"[GivePlayerWeaponSkin] Random: skinInfo.Count={skinInfo.Count}");

                    isLegacyModel = skinInfo.Count <= 0 || skinInfo[0].Value<bool>("legacy_model");
                    Logger.LogDebug($"[GivePlayerWeaponSkin] Random: isLegacyModel={isLegacyModel}");

                    UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: Random skin applied successfully");
                    return;
                }

                if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] EXIT: HasChangedPaint=false or weaponInfo=null, weaponDefIndex={weaponDefIndex}");
                    return;
                }

                Logger.LogDebug($"[GivePlayerWeaponSkin] Applying CUSTOM skin: Paint={weaponInfo.Paint}, Seed={weaponInfo.Seed}, Wear={weaponInfo.Wear}, StatTrak={weaponInfo.StatTrak}");

                // ========== GUARD: Re-validate weapon ==========
                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon invalid before custom skin apply");
                    return;
                }

                weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
                weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();

                UpdatePlayerEconItemId(weapon.AttributeManager.Item);

                weapon.AttributeManager.Item.CustomName = weaponInfo.Nametag;
                weapon.FallbackPaintKit = weaponInfo.Paint;

                weapon.FallbackSeed = weaponInfo is { Paint: 38, Seed: 0 } ? _fadeSeed++ : weaponInfo.Seed;

                weapon.FallbackWear = weaponInfo.Wear;

                Logger.LogDebug($"[GivePlayerWeaponSkin] Invoking CAttributeListSetOrAddAttributeValueByName for paint={weapon.FallbackPaintKit}");
                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weapon.FallbackPaintKit);

                if (weaponInfo.StatTrak)
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] Applying StatTrak: count={weaponInfo.StatTrakCount}");
                    weapon.AttributeManager.Item.EntityQuality = 9;

                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);

                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);
                }

                fallbackPaintKit = weapon.FallbackPaintKit;

                if (fallbackPaintKit == 0)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: fallbackPaintKit==0");
                    return;
                }

                // ========== GUARD: Re-validate weapon ==========
                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: weapon invalid before keychain/stickers");
                    return;
                }

                if (weaponInfo.KeyChain != null)
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] Applying Keychain: Id={weaponInfo.KeyChain.Id}");
                    SetKeychain(player, weapon);
                }

                if (weaponInfo.Stickers.Count > 0)
                {
                    Logger.LogDebug($"[GivePlayerWeaponSkin] Applying Stickers: count={weaponInfo.Stickers.Count}");
                    SetStickers(player, weapon);
                }

                Logger.LogDebug($"[GivePlayerWeaponSkin] Searching SkinsList for defindex={weaponDefIndex}, paint={fallbackPaintKit}");
                skinInfo = SkinsList
                    .Where(w =>
                        w["weapon_defindex"]?.ToObject<int>() == weaponDefIndex &&
                        w["paint"]?.ToObject<int>() == fallbackPaintKit)
                    .ToList();

                Logger.LogDebug($"[GivePlayerWeaponSkin] SkinsList search result: count={skinInfo.Count}");

                isLegacyModel = skinInfo.Count <= 0 || skinInfo[0].Value<bool>("legacy_model");
                Logger.LogDebug($"[GivePlayerWeaponSkin] isLegacyModel={isLegacyModel}, calling UpdatePlayerWeaponMeshGroupMask");

                UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);
                Logger.LogDebug("[GivePlayerWeaponSkin] EXIT: Custom skin applied successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[GivePlayerWeaponSkin] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // silly method to update sticker when call RefreshWeapons()
        private void IncrementWearForWeaponWithStickers(CCSPlayerController player, CBasePlayerWeapon weapon)
        {
            try
            {
                if (weapon == null || !weapon.IsValid)
                {
                    Logger.LogDebug("[IncrementWearForWeaponWithStickers] weapon null or invalid");
                    return;
                }

                int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
                if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null ||
                    weaponInfo.Stickers.Count <= 0) return;

                float wearIncrement = 0.001f;
                float currentWear = weaponInfo.Wear;

                var playerWear = _temporaryPlayerWeaponWear.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<int, float>());

                float incrementedWear = playerWear.AddOrUpdate(
                    weaponDefIndex,
                    currentWear + wearIncrement,
                    (_, oldWear) => Math.Min(oldWear + wearIncrement, 1.0f)
                );

                weapon.FallbackWear = incrementedWear;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[IncrementWearForWeaponWithStickers] EXCEPTION: {ex.Message}");
            }
        }

        private void SetStickers(CCSPlayerController? player, CBasePlayerWeapon weapon)
        {
            Logger.LogDebug($"[SetStickers] ENTER: player={player?.PlayerName}, weapon.IsValid={weapon?.IsValid}");

            try
            {
                if (player == null || !player.IsValid)
                {
                    Logger.LogDebug("[SetStickers] EXIT: player null or invalid");
                    return;
                }

                if (weapon == null || !weapon.IsValid)
                {
                    Logger.LogDebug("[SetStickers] EXIT: weapon null or invalid");
                    return;
                }

                int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

                if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
                {
                    Logger.LogDebug("[SetStickers] EXIT: no paint data");
                    return;
                }

                Logger.LogDebug($"[SetStickers] Processing {weaponInfo.Stickers.Count} stickers");

                foreach (var sticker in weaponInfo.Stickers)
                {
                    // ========== GUARD: Re-validate weapon each iteration ==========
                    if (!weapon.IsValid)
                    {
                        Logger.LogDebug("[SetStickers] EXIT: weapon became invalid during sticker loop");
                        return;
                    }

                    int stickerSlot = weaponInfo.Stickers.IndexOf(sticker);
                    Logger.LogDebug($"[SetStickers] Applying sticker slot={stickerSlot}, id={sticker.Id}");

                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} id", ViewAsFloat(sticker.Id));
                    if (sticker.OffsetX != 0 || sticker.OffsetY != 0)
                        CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                            $"sticker slot {stickerSlot} schema", 0);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} offset x", sticker.OffsetX);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} offset y", sticker.OffsetY);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} wear", sticker.Wear);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} scale", sticker.Scale);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                        $"sticker slot {stickerSlot} rotation", sticker.Rotation);
                }

                if (_temporaryPlayerWeaponWear.TryGetValue(player.Slot, out var playerWear) &&
                    playerWear.TryGetValue(weaponDefIndex, out float storedWear))
                {
                    weapon.FallbackWear = storedWear;
                }

                Logger.LogDebug("[SetStickers] EXIT: success");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SetStickers] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SetKeychain(CCSPlayerController? player, CBasePlayerWeapon weapon)
        {
            Logger.LogDebug($"[SetKeychain] ENTER: player={player?.PlayerName}, weapon.IsValid={weapon?.IsValid}");

            try
            {
                if (player == null || !player.IsValid)
                {
                    Logger.LogDebug("[SetKeychain] EXIT: player null or invalid");
                    return;
                }

                if (weapon == null || !weapon.IsValid)
                {
                    Logger.LogDebug("[SetKeychain] EXIT: weapon null or invalid");
                    return;
                }

                int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

                if (!HasChangedPaint(player, weaponDefIndex, out var value) || value?.KeyChain == null)
                {
                    Logger.LogDebug("[SetKeychain] EXIT: no keychain data");
                    return;
                }

                var keyChain = value.KeyChain;
                Logger.LogDebug($"[SetKeychain] Applying keychain id={keyChain.Id}");

                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                    "keychain slot 0 id", ViewAsFloat(keyChain.Id));
                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                    "keychain slot 0 offset x", keyChain.OffsetX);
                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                    "keychain slot 0 offset y", keyChain.OffsetY);
                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                    "keychain slot 0 offset z", keyChain.OffsetZ);
                CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
                    "keychain slot 0 seed", ViewAsFloat(keyChain.Seed));

                Logger.LogDebug("[SetKeychain] EXIT: success");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SetKeychain] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void GiveKnifeToPlayer(CCSPlayerController? player)
        {
            if (!_config.Additional.KnifeEnabled || player == null || !player.IsValid) return;

            if (PlayerHasKnife(player)) return;

            player.GiveNamedItem(CsItem.Knife);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
        }

        private static bool PlayerHasKnife(CCSPlayerController? player)
        {
            if (!_config.Additional.KnifeEnabled) return false;

            if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
            {
                return false;
            }

            if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
                return false;

            var weapons = player.PlayerPawn.Value.WeaponServices?.MyWeapons;
            if (weapons == null) return false;
            foreach (var weapon in weapons)
            {
                if (!weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) continue;
                if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshWeapons(CCSPlayerController? player)
        {
            if (!_gBCommandsAllowed) return;
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE)
                return;
            if (player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
                return;

            var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;

            if (weapons.Count == 0)
                return;
            if (player.Team is CsTeam.None or CsTeam.Spectator)
                return;

            var hasKnife = false;

            Dictionary<string, List<(int, int)>> weaponsWithAmmo = [];

            foreach (var weapon in weapons)
            {
                if (!weapon.IsValid || weapon.Value == null ||
                    !weapon.Value.IsValid || !weapon.Value.DesignerName.Contains("weapon_"))
                    continue;

                CCSWeaponBaseGun gun = weapon.Value.As<CCSWeaponBaseGun>();

                if (weapon.Value.Entity == null) continue;
                if (!weapon.Value.OwnerEntity.IsValid) continue;
                if (gun.Entity == null) continue;
                if (!gun.IsValid) continue;

                try
                {
                    CCSWeaponBaseVData? weaponData = weapon.Value.As<CCSWeaponBase>().VData;

                    if (weaponData == null) continue;

                    if (weaponData.GearSlot is gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL)
                    {
                        if (!WeaponDefindex.TryGetValue(weapon.Value.AttributeManager.Item.ItemDefinitionIndex, out var weaponByDefindex))
                            continue;

                        int clip1 = weapon.Value.Clip1;
                        int reservedAmmo = weapon.Value.ReserveAmmo[0];

                        if (!weaponsWithAmmo.TryGetValue(weaponByDefindex, out var value))
                        {
                            value = [];
                            weaponsWithAmmo.Add(weaponByDefindex, value);
                        }

                        value.Add((clip1, reservedAmmo));

                        if (gun.VData == null) return;

                        weapon.Value?.AddEntityIOEvent("Kill", weapon.Value, null, "", 0.1f);
                    }

                    if (weaponData.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE)
                    {
                        weapon.Value?.AddEntityIOEvent("Kill", weapon.Value, null, "", 0.1f);
                        hasKnife = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex.Message);
                }
            }

            AddTimer(0.23f, () =>
                    {
                        if (!_gBCommandsAllowed) return;

                        if (!PlayerHasKnife(player) && hasKnife)
                        {
                            var newKnife = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.Knife));
                            var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.USP));
                            player.GiveNamedItem(CsItem.Knife);
                            player.ExecuteClientCommand("slot3");

                            Server.NextFrame(() =>
                            {
                                try
                                {
                                    if (newKnife != null && newKnife.IsValid)
                                        newKnife.AddEntityIOEvent("Kill", newKnife, null, "", 0.01f);
                                    if (newWeapon != null && newWeapon.IsValid)
                                        newWeapon.AddEntityIOEvent("Kill", newWeapon, null, "", 0.01f);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogWarning("Error AddEntityIOEvent " + ex.Message);
                                }
                            });
                        }


                        foreach (var entry in weaponsWithAmmo)
                        {
                            foreach (var ammo in entry.Value)
                            {
                                var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(entry.Key));
                                Server.NextFrame(() =>
                        {
                            try
                            {
                                newWeapon.Clip1 = ammo.Item1;
                                newWeapon.ReserveAmmo[0] = ammo.Item2;

                                IncrementWearForWeaponWithStickers(player, newWeapon);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning("Error setting weapon properties: " + ex.Message);
                            }
                        });
                            }
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void GivePlayerGloves(CCSPlayerController player)
        {
            if (!Utility.IsPlayerValid(player) || (LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE) return;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                return;

            var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? string.Empty;
            if (!string.IsNullOrEmpty(model))
            {
                pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
                pawn.SetModel(model);
            }

            CEconItemView item = pawn.EconGloves;

            item.NetworkedDynamicAttributes.Attributes.RemoveAll();
            item.AttributeList.Attributes.RemoveAll();

            Instance.AddTimer(0.08f, () =>
            {
                try
                {
                    if (!player.IsValid)
                        return;

                    if (!player.PawnIsAlive)
                        return;

                    if (!GPlayersGlove.TryGetValue(player.Slot, out var gloveInfo) ||
                        !gloveInfo.TryGetValue(player.Team, out var gloveId) ||
                        gloveId == 0 ||
                        !HasChangedPaint(player, gloveId, out var weaponInfo) || weaponInfo == null)
                        return;

                    item.ItemDefinitionIndex = gloveId;

                    UpdatePlayerEconItemId(item);

                    item.NetworkedDynamicAttributes.Attributes.RemoveAll();
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weaponInfo.Paint);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture seed", weaponInfo.Seed);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture wear", weaponInfo.Wear);

                    item.AttributeList.Attributes.RemoveAll();
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture prefab", weaponInfo.Paint);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture seed", weaponInfo.Seed);
                    CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture wear", weaponInfo.Wear);

                    item.Initialized = true;

                    SetBodygroup(pawn, "default_gloves", 1);
                }
                catch (Exception) { }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static int GetRandomPaint(int defindex)
        {
            if (SkinsList.Count == 0)
                return 0;

            Random rnd = new Random();

            // Filter weapons by the provided defindex
            var filteredWeapons = SkinsList.Where(w => w["weapon_defindex"]?.ToString() == defindex.ToString()).ToList();

            if (filteredWeapons.Count == 0)
                return 0;

            var randomWeapon = filteredWeapons[rnd.Next(filteredWeapons.Count)];

            return int.TryParse(randomWeapon["paint"]?.ToString(), out var paintValue) ? paintValue : 0;
        }

        //xstage idea on css discord
        public static void SubclassChange(CBasePlayerWeapon weapon, ushort itemD)
        {
            Instance.Logger.LogDebug($"[SubclassChange] ENTER: weapon.IsValid={weapon?.IsValid}, itemD={itemD}");

            try
            {
                // ========== GUARD: Validate weapon ==========
                if (weapon == null)
                {
                    Instance.Logger.LogDebug("[SubclassChange] EXIT: weapon is null");
                    return;
                }

                if (!weapon.IsValid)
                {
                    Instance.Logger.LogDebug("[SubclassChange] EXIT: weapon.IsValid=false");
                    return;
                }

                Instance.Logger.LogDebug($"[SubclassChange] Calling AcceptInput ChangeSubclass with itemD={itemD}");
                weapon.AcceptInput("ChangeSubclass", value: itemD.ToString());
                Instance.Logger.LogDebug("[SubclassChange] EXIT: success");
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError($"[SubclassChange] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void SetBodygroup(CCSPlayerPawn pawn, string group, int value)
        {
            try
            {
                if (pawn == null || !pawn.IsValid)
                {
                    Instance.Logger.LogDebug("[SetBodygroup] EXIT: pawn null or invalid");
                    return;
                }

                pawn.AcceptInput("SetBodygroup", value: $"{group},{value}");
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError($"[SetBodygroup] EXCEPTION: {ex.Message}");
            }
        }

        private void UpdateWeaponMeshGroupMask(CBaseEntity weapon, bool isLegacy = false)
        {
            Logger.LogDebug($"[UpdateWeaponMeshGroupMask] ENTER: weapon.IsValid={weapon?.IsValid}, isLegacy={isLegacy}");

            try
            {
                // ========== GUARD: Validate weapon ==========
                if (weapon == null)
                {
                    Logger.LogDebug("[UpdateWeaponMeshGroupMask] EXIT: weapon is null");
                    return;
                }

                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[UpdateWeaponMeshGroupMask] EXIT: weapon.IsValid=false");
                    return;
                }

                if (weapon.CBodyComponent?.SceneNode == null)
                {
                    Logger.LogDebug("[UpdateWeaponMeshGroupMask] EXIT: CBodyComponent or SceneNode is null");
                    return;
                }

                // ========== GUARD: Double-check IsValid right before native call ==========
                if (!weapon.IsValid)
                {
                    Logger.LogDebug("[UpdateWeaponMeshGroupMask] EXIT: weapon became invalid before AcceptInput");
                    return;
                }

                string bodyValue = $"body,{(isLegacy ? 1 : 0)}";
                Logger.LogDebug($"[UpdateWeaponMeshGroupMask] Calling AcceptInput SetBodygroup value={bodyValue}");

                weapon.AcceptInput("SetBodygroup", value: bodyValue);

                Logger.LogDebug("[UpdateWeaponMeshGroupMask] EXIT: success");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[UpdateWeaponMeshGroupMask] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void UpdatePlayerWeaponMeshGroupMask(CCSPlayerController player, CBasePlayerWeapon weapon, bool isLegacy)
        {
            Logger.LogDebug($"[UpdatePlayerWeaponMeshGroupMask] ENTER: player={player?.PlayerName}, weapon.IsValid={weapon?.IsValid}, isLegacy={isLegacy}");

            // ========== GUARD: Final validation before mesh update ==========
            if (weapon == null || !weapon.IsValid)
            {
                Logger.LogDebug("[UpdatePlayerWeaponMeshGroupMask] EXIT: weapon null or invalid");
                return;
            }

            UpdateWeaponMeshGroupMask(weapon, isLegacy);
        }

        private static void GivePlayerAgent(CCSPlayerController player)
        {
            if (!GPlayersAgent.TryGetValue(player.Slot, out var value)) return;

            var model = player.TeamNum == 3 ? value.CT : value.T;
            if (string.IsNullOrEmpty(model)) return;

            if (player.PlayerPawn.Value == null)
                return;

            try
            {
                Server.NextFrame(() =>
                {
                    player.PlayerPawn.Value.SetModel(
                        $"characters/models/{model}.vmdl"
                    );
                });
            }
            catch (Exception)
            {
            }
        }

        private static void GivePlayerMusicKit(CCSPlayerController player)
        {
            if (player.IsBot) return;
            if (!GPlayersMusic.TryGetValue(player.Slot, out var musicInfo) ||
                !musicInfo.TryGetValue(player.Team, out var musicId) || musicId == 0) return;

            if (player.InventoryServices == null) return;

            player.MusicKitID = musicId;
            player.InventoryServices.MusicID = musicId;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitID");
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
        }

        private static void GivePlayerPin(CCSPlayerController player)
        {
            if (!GPlayersPin.TryGetValue(player.Slot, out var pinInfo) ||
                !pinInfo.TryGetValue(player.Team, out var pinId)) return;
            if (player.InventoryServices == null) return;

            player.InventoryServices.Rank[5] = pinId > 0 ? (MedalRank_t)pinId : MedalRank_t.MEDAL_RANK_NONE;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
        }

        private void GiveOnItemPickup(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null) return;

            var myWeapons = pawn.WeaponServices?.MyWeapons;
            if (myWeapons == null) return;

            foreach (var handle in myWeapons)
            {
                var weapon = handle.Value;

                if (weapon == null || !weapon.IsValid) continue;
                if (myWeapons.Count == 1)
                {
                    var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.USP));
                    weapon.AddEntityIOEvent("Kill", weapon, null, "", 0.01f);
                    player.GiveNamedItem(CsItem.Knife);
                    player.ExecuteClientCommand("slot3");
                    newWeapon.AddEntityIOEvent("Kill", newWeapon, null, "", 0.01f);
                }

                GivePlayerWeaponSkin(player, weapon);
            }
        }

        private void UpdatePlayerEconItemId(CEconItemView econItemView)
        {
            var itemId = _nextItemId++;

            econItemView.ItemID = itemId;
            econItemView.ItemIDLow = (uint)itemId & 0xFFFFFFFF;
            econItemView.ItemIDHigh = (uint)itemId >> 32;
        }

        private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
        {
            try
            {
                var pawn = itemServices.Pawn.Value;
                if (!pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value == null) return null;
                var player = new CCSPlayerController(pawn.Controller.Value.Handle);
                return !Utility.IsPlayerValid(player) ? null : player;
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError($"[GetPlayerFromItemServices] EXCEPTION: {ex.Message}");
                return null;
            }
        }

        private static bool HasChangedKnife(CCSPlayerController player, out string? knifeValue)
        {
            knifeValue = null;

            // Check if player has knife info for their slot and team
            if (!GPlayersKnife.TryGetValue(player.Slot, out var knife) ||
                !knife.TryGetValue(player.Team, out var value) ||
                value == "weapon_knife") return false;
            knifeValue = value; // Assign the knife value to the out parameter
            return true;
        }

        private static bool HasChangedPaint(CCSPlayerController player, int weaponDefIndex, out WeaponInfo? weaponInfo)
        {
            weaponInfo = null;

            // Check if player has weapons info for their slot and team
            if (!GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teamInfo) ||
                !teamInfo.TryGetValue(player.Team, out var teamWeapons))
            {
                return false;
            }

            // Check if the specified weapon has a paint/skin change
            if (!teamWeapons.TryGetValue(weaponDefIndex, out var value) || value.Paint <= 0) return false;

            weaponInfo = value; // Assign the out variable when it exists
            return true;
        }

        private static float ViewAsFloat(uint value)
        {
            return BitConverter.Int32BitsToSingle((int)value);
        }
    }
}
