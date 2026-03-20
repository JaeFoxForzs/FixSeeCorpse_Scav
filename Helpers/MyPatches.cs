using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KrokoshaCasualtiesMP;

namespace FixSeeCorpse.Helpers
{
    public static class MyPatches
    {
        private const float FieldOfViewAngle = 60f;
        private const float ScreenMargin = 0.9f;
        private const float DarknessThreshold = 0.05f;
        private const float SearchRadius = 50f;
        
        // --- Настройки механики SeeTicks ---
        private const float MinNeedSeeTicks = 0.15f; 
        private const float MaxNeedSeeTicks = 1.5f;  
        
        private const float MinNoticeDistance = 10f;  
        private const float MaxNoticeDistance = 45f; 
        // -----------------------------------------

        private static readonly Vector2 CorpseOffset = new Vector2(0f, 1f);

        private static Limb cachedHeadLimb;
        private static Body cachedHeadOwner;

        private static FieldInfo _peopleSeenFieldInfo;
        private static FieldInfo _animalCorpseFieldInfo;

        private static Dictionary<Vector2Int, BrightnessData> _brightnessCache = new Dictionary<Vector2Int, BrightnessData>();
        private static MonoBehaviour _coroutineRunner;
        private const float BrightnessCacheLifetime = 0.2f;

        private static Texture2D _reusablePixelTexture;

        // Состояние обзора трупов для каждого игрока
        private class ViewState
        {
            public float SeeTicks;
            public float LastTickTime;
            public bool IsSpottedCached; // Кэшируем результат, чтобы не спамить в одном кадре
        }
        
        private static Dictionary<int, ViewState> _corpseViewStates = new Dictionary<int, ViewState>();
        private static float _lastCleanupTime;

        private struct BrightnessData
        {
            public float brightness;
            public float timestamp;
        }

        #region CorpseScript (Singleplayer / Host)
        
        public static bool CorpseScript_OnWillRenderObject_Prefix(
            CorpseScript __instance,
            ref bool ___didComment)
        {
            if (PlayerCamera.main == null || PlayerCamera.main.body == null)
                return false;

            Body player = PlayerCamera.main.body;
            Vector2 corpsePos = __instance.transform.position;

            EnsureBrightnessUpdate(__instance, corpsePos);

            bool isSpotted = CheckAndProcessCorpseView(player, __instance, corpsePos);

            if (isSpotted)
            {
                if (!__instance.animalCorpse && !___didComment)
                {
                    ___didComment = true;

                    __instance.GetComponent<BuildingEntity>().fullName = Locale.GetBuilding("corpseseen");

                    player.happiness -= 3.5f * player.desensitizedMult;
                    player.sicknessAmount += 6f * player.desensitizedMult;
                    player.desensitizedMult *= 0.9f;
                    player.corpsesSeen++;

                    if (player.totalHappiness < -55f)
                    {
                        player.talker.Talk(Locale.GetCharacter("seecorpsesuicidal"), null, true, false);
                    }
                    else if (player.corpsesSeen < 9)
                    {
                        player.talker.Talk(Locale.GetCharacter("seecorpse"), null, false, false);
                        player.eyeScareTime = 4f;
                    }
                    else
                    {
                        player.talker.Talk(Locale.GetCharacter("seecorpsedesensitized"), null, false, false);
                    }
                }

                player.overrideLookTime = 0.5f;
                player.overrideLookPos = corpsePos;

                if (player.corpsesSeen < 9)
                {
                    player.eyeScareTime = 0.5f;
                }
            }

            return false;
        }
        
        #endregion

        #region Krokosha_CorpseScript_MultiplayerAdditionComponent (Multiplayer Client)
        
        public static bool Krokosha__CorpseScript__MultiplayerAdditionComponent_SlowUpdate_Prefix(
            MonoBehaviour __instance)
        {
            if (_animalCorpseFieldInfo == null)
            {
                _animalCorpseFieldInfo = __instance.GetType().GetField("animalCorpse", 
                    BindingFlags.Public | BindingFlags.Instance);
            }
            if (_peopleSeenFieldInfo == null)
            {
                _peopleSeenFieldInfo = __instance.GetType().GetField("people_that_seen_it", 
                    BindingFlags.Public | BindingFlags.Instance);
            }

            bool animalCorpse = (bool)_animalCorpseFieldInfo.GetValue(__instance);
            HashSet<NetBody> peopleSeenIt = (HashSet<NetBody>)_peopleSeenFieldInfo.GetValue(__instance);

            if (animalCorpse)
                return false;

            NetBody corpseNetBody = null;
            __instance.TryGetComponent<NetBody>(out corpseNetBody);
            
            if (corpseNetBody != null)
            {
                if (corpseNetBody.body.alive)
                {
                    Object.Destroy(__instance);
                    return false;
                }
                if (corpseNetBody.timeHasBeenDead < 2.0)
                {
                    return false;
                }
            }

            Vector2 corpsePos = __instance.transform.position;

            EnsureBrightnessUpdate(__instance, corpsePos);

            foreach (NetBody netBody in NetPlayer.GetPlayerBodiesInRadius(corpsePos, SearchRadius))
            {
                if (peopleSeenIt.Contains(netBody))
                    continue;

                Body player = netBody.body;
                
                bool isSpotted = CheckAndProcessCorpseView(player, __instance, corpsePos);

                if (isSpotted)
                {
                    peopleSeenIt.Add(netBody);

                    if (!netBody.is_local || corpseNetBody != null)
                    {
                        player.happiness -= 3.5f * player.desensitizedMult;
                        player.sicknessAmount += 6f * player.desensitizedMult;
                        player.desensitizedMult *= 0.9f;
                        player.corpsesSeen++;

                        if (player.totalHappiness < -55f)
                        {
                            player.talker.Talk(Locale.GetCharacter("seecorpsesuicidal"), null, true, false);
                        }
                        else if (player.corpsesSeen < 9)
                        {
                            player.talker.Talk(Locale.GetCharacter("seecorpse"), null, false, false);
                            player.eyeScareTime = 4f;
                        }
                        else
                        {
                            player.talker.Talk(Locale.GetCharacter("seecorpsedesensitized"), null, false, false);
                        }
                    }
                }
            }

            return false;
        }

        public static bool Krokosha__CorpseScript__MultiplayerAdditionComponent_OnWillRenderObject_Prefix(
            MonoBehaviour __instance)
        {
            Vector2 corpsePos = __instance.transform.position;

            foreach (NetBody netBody in NetPlayer.GetPlayerBodiesInRadius(corpsePos, SearchRadius))
            {
                Body player = netBody.body;
                
                bool isSpotted = CheckAndProcessCorpseView(player, __instance, corpsePos);

                if (isSpotted)
                {
                    player.overrideLookTime = 0.5f;
                    player.overrideLookPos = corpsePos;

                    if (player.corpsesSeen < 9)
                    {
                        player.eyeScareTime = 0.5f;
                    }
                }
            }

            return false;
        }
        
        #endregion

        #region SeeTicks Logic

        private static int GetViewKey(Body player, MonoBehaviour corpse)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + player.GetInstanceID();
                hash = hash * 31 + corpse.GetInstanceID();
                return hash;
            }
        }

        private static bool CheckAndProcessCorpseView(Body player, MonoBehaviour corpse, Vector2 corpsePos)
        {
            CleanupOldViewStates();

            int viewKey = GetViewKey(player, corpse);
            
            if (!_corpseViewStates.TryGetValue(viewKey, out ViewState state))
            {
                state = new ViewState();
                state.LastTickTime = Time.time - 0.01f; // Смещение для первого кадра
                _corpseViewStates[viewKey] = state;
            }

            float timeSinceLastTick = Time.time - state.LastTickTime;

            // Если функция вызывается мультиплеерным модом несколько раз 
            // в один и тот же кадр, просто отдаем результат и не трогаем таймер!
            if (timeSinceLastTick == 0f)
            {
                return state.IsSpottedCached;
            }

            if (timeSinceLastTick > 0.5f)
            {
                state.SeeTicks = 0f;
            }

            bool canSeeCorpse = CanPlayerSeeCorpse(player, corpsePos);

            if (canSeeCorpse)
            {
                float distance = Vector2.Distance(player.transform.position, corpsePos);
                float distanceFactor = Mathf.InverseLerp(MinNoticeDistance, MaxNoticeDistance, distance);
                float ConsciousnessFactor = Mathf.Lerp(3.5f, 0f, player.consciousness / 100f);
                float needSeeTicks = Mathf.Lerp(MinNeedSeeTicks, MaxNeedSeeTicks, distanceFactor) + ConsciousnessFactor;

                if (timeSinceLastTick < 0.5f)
                {
                    state.SeeTicks += timeSinceLastTick;
                }
                
                state.LastTickTime = Time.time;



                if (state.SeeTicks >= needSeeTicks)
                {
                    state.IsSpottedCached = true;
                    return true;
                }
            }
            else
            {
                state.SeeTicks = 0f;
                state.LastTickTime = Time.time;
            }

            state.IsSpottedCached = false;
            return false;
        }

        private static void CleanupOldViewStates()
        {
            if (Time.time - _lastCleanupTime > 10f)
            {
                _lastCleanupTime = Time.time;
                List<int> keysToRemove = new List<int>();
                
                foreach (var kvp in _corpseViewStates)
                {
                    if (Time.time - kvp.Value.LastTickTime > 10f)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (int key in keysToRemove)
                {
                    _corpseViewStates.Remove(key);
                }
            }
        }

        #endregion

        #region Shared Helper Methods

        private static bool CanPlayerSeeCorpse(Body player, Vector2 corpsePos)
        {
            // Проверяем, локальный ли это игрок (т.е. тот, кто играет за этим компьютером)
            bool isLocalPlayer = (PlayerCamera.main != null && player == PlayerCamera.main.body);

            // 1. Математика: Угол обзора (работает для всех, независимо от монитора)
            if (!IsPlayerLookingAt(player, corpsePos))
                return false;
            
            // 2. Физика: Линия видимости через Raycast (работает для всех)
            Vector2 playerEyePos = GetPlayerEyePosition(player);
            Vector2 corpseTargetPos = corpsePos + CorpseOffset;
            if (!HasLineOfSight(playerEyePos, corpseTargetPos))
                return false;
            
            // ИСПРАВЛЕНИЕ: Проверяем освещение экрана и рамки камеры ТОЛЬКО для локального игрока.
            // Для игроков по сети мы не знаем, что у них рендерится на экране, поэтому считаем, 
            // что если они смотрят на труп и нет стен - они его видят.
            if (isLocalPlayer)
            {
                if (!IsInCameraView(corpsePos))
                    return false;
                
                if (!IsIlluminatedCached(corpseTargetPos))
                    return false;
            }

            return true;
        }

        private static Vector2 GetPlayerEyePosition(Body player)
        {
            if (cachedHeadOwner != player || cachedHeadLimb == null)
            {
                cachedHeadOwner = player;
                cachedHeadLimb = null;
                
                foreach (Limb limb in player.limbs)
                {
                    if (limb != null && limb.isHead && !limb.dismembered)
                    {
                        cachedHeadLimb = limb;
                        break;
                    }
                }
            }
            
            if (cachedHeadLimb != null && cachedHeadLimb.gameObject.activeSelf)
            {
                return cachedHeadLimb.transform.position;
            }
            
            return (Vector2)player.transform.position + new Vector2(0f, 2.5f);
        }

        private static bool IsInCameraView(Vector2 worldPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            Vector3 viewportPos = cam.WorldToViewportPoint(worldPos);
            float margin = (1f - ScreenMargin) / 2f;

            bool inX = viewportPos.x > margin && viewportPos.x < (1f - margin);
            bool inY = viewportPos.y > margin && viewportPos.y < (1f - margin);
            bool inFront = viewportPos.z > 0;

            return inX && inY && inFront;
        }

        private static bool IsPlayerLookingAt(Body player, Vector2 targetPos)
        {
            Vector2 playerPos = player.transform.position;
            Vector2 directionToTarget = (targetPos - playerPos).normalized;
            Vector2 lookDirection = GetPlayerLookDirection(player);
            float angle = Vector2.Angle(lookDirection, directionToTarget);

            return angle <= FieldOfViewAngle;
        }

        private static Vector2 GetPlayerLookDirection(Body player)
        {
            Vector2 playerPos = player.transform.position;
            Vector2 targetPos = player.targetLookPos;
            Vector2 direction = targetPos - playerPos;

            if (direction.sqrMagnitude < 0.001f)
            {
                return player.isRight ? Vector2.right : Vector2.left;
            }

            return direction.normalized;
        }

        private static bool HasLineOfSight(Vector2 from, Vector2 to)
        {
            RaycastHit2D hit = Physics2D.Linecast(from, to, LayerMask.GetMask("Ground"));
            return hit.collider == null;
        }

        #region Brightness Cache

        private static void EnsureBrightnessUpdate(MonoBehaviour runner, Vector2 corpsePos)
        {
            if (_coroutineRunner == null || !_coroutineRunner.gameObject.activeInHierarchy)
            {
                _coroutineRunner = runner;
            }

            Vector2Int cacheKey = new Vector2Int(
                Mathf.RoundToInt(corpsePos.x),
                Mathf.RoundToInt(corpsePos.y)
            );

            bool needsUpdate = true;
            if (_brightnessCache.TryGetValue(cacheKey, out BrightnessData data))
            {
                if (Time.time - data.timestamp < BrightnessCacheLifetime)
                {
                    needsUpdate = false;
                }
            }

            if (needsUpdate && _coroutineRunner != null)
            {
                _coroutineRunner.StartCoroutine(UpdateBrightnessCache(corpsePos + CorpseOffset));
            }
        }

        private static IEnumerator UpdateBrightnessCache(Vector2 worldPosition)
        {
            yield return new WaitForEndOfFrame();

            Camera cam = Camera.main;
            if (cam == null) yield break;

            Vector3 screenPos = cam.WorldToScreenPoint(worldPosition);
            Vector2Int cacheKey = new Vector2Int(
                Mathf.RoundToInt(worldPosition.x),
                Mathf.RoundToInt(worldPosition.y)
            );

            float brightness = 1f;

            if (screenPos.x >= 0 && screenPos.x < Screen.width &&
                screenPos.y >= 0 && screenPos.y < Screen.height &&
                screenPos.z > 0)
            {
                try
                {
                    if (_reusablePixelTexture == null)
                    {
                        _reusablePixelTexture = new Texture2D(1, 1, TextureFormat.RGB24, false);
                    }

                    _reusablePixelTexture.ReadPixels(new Rect(screenPos.x, screenPos.y, 1, 1), 0, 0, false);
                    _reusablePixelTexture.Apply();
                    
                    Color pixel = _reusablePixelTexture.GetPixel(0, 0);
                    
                    brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                }
                catch
                {
                    brightness = 1f;
                }
            }

            _brightnessCache[cacheKey] = new BrightnessData
            {
                brightness = brightness,
                timestamp = Time.time
            };
        }

        private static bool IsIlluminatedCached(Vector2 worldPosition)
        {
            Vector2Int cacheKey = new Vector2Int(
                Mathf.RoundToInt(worldPosition.x),
                Mathf.RoundToInt(worldPosition.y)
            );

            if (_brightnessCache.TryGetValue(cacheKey, out BrightnessData data))
            {
                if (Time.time - data.timestamp < BrightnessCacheLifetime * 3f)
                {
                    return data.brightness > DarknessThreshold;
                }
            }

            return true;
        }

        #endregion
        
        #endregion
    }
}