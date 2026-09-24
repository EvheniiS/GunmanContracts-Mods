using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

[assembly: MelonInfo(typeof(ArrowGrabAssist.ArrowGrabAssistMod), "ArrowGrabAssist", "1.1.1", "Evhenii")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace ArrowGrabAssist
{
    // Why this exists (from reading the game's HurricaneVR code):
    //  * HVRHandGrabber.CheckGrab only grabs on the grip-PRESS frame (IsGripGrabActivated).
    //  * HVRHandGrabber.CanHover refuses any NEW hover target while grip is HELD.
    //  So a grip press that lands a moment before the bow string becomes the hand's hover
    //  target is lost, and hover stays locked until the grip is released and pressed again.
    //  Fix 1: buffer the press - if grip is held, the hand is empty, the press was recent and
    //         the hand is near the string of a bow held in the other hand, call TryGrab on the
    //         string (the game's own path: CanGrab checks, grab events, HVRArrowLoader spawn).
    //  Fix 2: HVRPhysicsBow clears the previous arrow only after the next FixedUpdate, and
    //         HVRArrowLoader spawns a new one only if the slot is empty. If the string was
    //         grabbed inside that window, re-run the loader once the slot is empty.
    //  Fix 3: explosive barrels need two arrows. Arrows damage breakables through
    //         ANBKnife.stabEnemy -> ANBBreakable.hit with the raw knifeDamage (bullets and
    //         explosions get a multiplier, arrows get none), and the barrel breaks only when
    //         health <= 0. See ExplosiveArrowPatches below.
    public class ArrowGrabAssistMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> ExplosiveArrows;
        internal static MelonPreferences_Entry<bool> DebugLogEntry;
        internal static MelonLogger.Instance Log;
        MelonPreferences_Entry<bool> _enabled, _retrySpawn, _debug;
        MelonPreferences_Entry<float> _radius, _buffer, _retryWindow;

        Type _tBow, _tHand, _tLoader, _tUnityObject;
        MethodInfo _findObjectsOfType, _il2cppTypeFrom, _castGeneric;
        bool _typesReady, _typesFailed;

        readonly List<object> _bows = new();
        readonly List<object> _hands = new();
        readonly Dictionary<IntPtr, object> _loaderByBow = new();
        readonly Dictionary<IntPtr, HandState> _handState = new();
        readonly Dictionary<IntPtr, BowState> _bowState = new();

        readonly Stopwatch _clock = Stopwatch.StartNew();
        double _nextScan;
        string _lastScan;
        long _frame;

        class HandState { public bool GripWasHeld; public double PressTime = -1; public long PressFrame; public bool Used; }
        class BowState { public bool NockWasHeld; public double GrabTime; public bool Retried; }

        double Now => _clock.Elapsed.TotalSeconds;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var cat = MelonPreferences.CreateCategory("ArrowGrabAssist");
            _enabled = cat.CreateEntry("Enabled", true, description: "Master switch.");
            _radius = cat.CreateEntry("GrabRadius", 0.15f, description: "Metres from your hand to the bow string's nock point within which a missed grip press is completed.");
            _buffer = cat.CreateEntry("PressBufferSeconds", 0.35f, description: "How long after pressing grip (while still holding it) the assist may still complete the grab.");
            _retrySpawn = cat.CreateEntry("RetryArrowSpawn", true, description: "Spawn the arrow if the string was grabbed while the previous arrow was still being cleared.");
            _retryWindow = cat.CreateEntry("RetryWindowSeconds", 0.5f, description: "How long after grabbing the string the arrow-spawn retry is allowed.");
            ExplosiveArrows = cat.CreateEntry("ExplosiveArrowsDetonateBarrels", true, description: "One arrow hit detonates an explosive barrel (the game otherwise needs two).");
            _debug = cat.CreateEntry("DebugLog", false, description: "Log every assisted grab / spawn / barrel hit to the MelonLoader console.");
            DebugLogEntry = _debug;
            LoggerInstance.Msg("loaded - grip-press buffer for the bow string, one-arrow explosive barrels.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _nextScan = Now + 3.0;
            _handState.Clear();
            _bowState.Clear();
        }

        public override void OnUpdate()
        {
            if (!_enabled.Value || _typesFailed) return;
            _frame++;
            if (!_typesReady && !ResolveTypes()) return;

            // Bows, loaders and hands register themselves on Start (Harmony postfixes below), so a
            // periodic scene search isn't needed. One search per scene remains as a safety net.
            if (_nextScan >= 0 && Now >= _nextScan) { _nextScan = -1; Rescan(); }
            DrainRegistrations();
            // Nothing re-scans any more, so destroyed objects must leave the lists here
            // (a dead bow would otherwise throw inside UpdateHand and get the hand dropped).
            for (int i = 0; i < _bows.Count; i++) if (!IsAlive(_bows[i])) _bows.RemoveAt(i--);
            for (int i = 0; i < _hands.Count; i++) if (!IsAlive(_hands[i])) _hands.RemoveAt(i--);
            if (_bows.Count == 0) return;

            for (int i = 0; i < _hands.Count; i++)
            {
                try { UpdateHand(_hands[i]); }
                catch (Exception e) { Drop(ref i, _hands, e); }
            }
            if (_retrySpawn.Value)
            {
                for (int i = 0; i < _bows.Count; i++)
                {
                    try { UpdateBowSpawn(_bows[i]); }
                    catch (Exception e) { Drop(ref i, _bows, e); }
                }
            }
        }

        void Drop(ref int i, List<object> list, Exception e)
        {
            if (_debug.Value) LoggerInstance.Warning($"dropping stale object: {e.GetType().Name}: {e.Message}");
            list.RemoveAt(i); i--;
        }

        // ---- Fix 1: buffered grip press near the string ----------------------------------
        void UpdateHand(object handObj)
        {
            IntPtr hp = ((Il2CppObjectBase)handObj).Pointer;
            if (!_handState.TryGetValue(hp, out var st)) _handState[hp] = st = new HandState();

            bool held = (bool)Get(handObj, "IsGripGrabActive");
            if (held && !st.GripWasHeld) { st.PressTime = Now; st.PressFrame = _frame; st.Used = false; }
            st.GripWasHeld = held;

            if (!held || st.Used || st.PressTime < 0) return;
            if (_frame == st.PressFrame) return;                     // give the game its own frame first
            if (Now - st.PressTime > _buffer.Value) return;
            if ((bool)Get(handObj, "IsGrabbing")) { st.Used = true; return; }   // game grabbed something itself

            var handPos = PositionOf(Get(handObj, "Palm")) ?? PositionOf(Get(handObj, "transform"));
            if (handPos == null) return;

            object best = null, bestBow = null; float bestDist = _radius.Value;
            foreach (var bowObj in _bows)
            {
                var nock = Get(bowObj, "NockGrabbable");
                if (nock == null) continue;
                if ((bool)Get(nock, "IsBeingHeld")) continue;
                // BowHand is set by HVRBowBase.OnHandGrabbed and cleared on release, so it is
                // "held in a hand" (Grabbable.IsBeingHeld would also count holster sockets).
                var bowHand = Get(bowObj, "BowHand");
                if (bowHand == null || ((Il2CppObjectBase)bowHand).Pointer == hp) continue;
                var np = PositionOf(Get(nock, "transform"));
                if (np == null) continue;
                float dist = Dist(handPos.Value, np.Value);
                if (dist < bestDist) { bestDist = dist; best = nock; bestBow = bowObj; }
            }
            if (best == null) return;

            // The bow learns its NockHand in BeforeNockHovered; this grab may never have hovered.
            Set(bestBow, "NockHand", handObj);
            bool ok = (bool)Call(handObj, "TryGrab", best, false);
            st.Used = ok;
            if (_debug.Value)
                LoggerInstance.Msg($"assisted string grab {(ok ? "OK" : "refused by CanGrab")} " +
                                   $"({(Now - st.PressTime) * 1000:0} ms after press, {bestDist * 100:0.0} cm)");
        }

        // ---- Fix 2: arrow spawn retry --------------------------------------------------
        void UpdateBowSpawn(object bowObj)
        {
            IntPtr bp = ((Il2CppObjectBase)bowObj).Pointer;
            if (!_loaderByBow.TryGetValue(bp, out var loaderObj)) return;
            if (!_bowState.TryGetValue(bp, out var st)) _bowState[bp] = st = new BowState();

            var nock = Get(bowObj, "NockGrabbable");
            if (nock == null) return;
            bool held = (bool)Get(nock, "IsBeingHeld");
            if (held && !st.NockWasHeld) { st.GrabTime = Now; st.Retried = false; }
            st.NockWasHeld = held;
            if (!held || st.Retried || Now - st.GrabTime > _retryWindow.Value) return;
            var arrow = Get(bowObj, "Arrow");
            if (arrow != null && IsAlive(arrow)) return;             // arrow is there - nothing to do

            var hand = Get(nock, "PrimaryGrabber") ?? Get(bowObj, "NockHand");
            hand = TryCastTo((Il2CppObjectBase)hand, _tHand);        // PrimaryGrabber may be a socket
            if (hand == null) return;
            st.Retried = true;
            Call(loaderObj, "OnStringGrabbed", hand, nock);          // game's own spawn path (checks ammo)
            if (_debug.Value) LoggerInstance.Msg($"arrow spawn retry -> arrow {(Get(bowObj, "Arrow") != null ? "created" : "not created (no ammo?)")}");
        }

        // ---- helpers ---------------------------------------------------------------------
        static (float x, float y, float z)? PositionOf(object transform)
        {
            if (transform == null) return null;
            var p = Get(transform, "position");
            return ((float)Get(p, "x"), (float)Get(p, "y"), (float)Get(p, "z"));
        }

        // Il2CppInterop exposes game fields as properties; some (IsGripGrabActive) are protected
        // in the game, so look members up regardless of visibility and cache them.
        static readonly Dictionary<(Type, string), Func<object, object>> _getters = new();
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        static object Get(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            if (!_getters.TryGetValue((t, name), out var g))
            {
                PropertyInfo pi = null; FieldInfo fi = null;
                for (var c = t; c != null && pi == null && fi == null; c = c.BaseType)
                {
                    pi = c.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                    if (pi == null) fi = c.GetField(name, Any | BindingFlags.DeclaredOnly);
                }
                if (pi != null) g = o => pi.GetValue(o);
                else if (fi != null) g = o => fi.GetValue(o);
                else throw new MissingMemberException(t.FullName, name);
                _getters[(t, name)] = g;
            }
            return g(obj);
        }

        static void Set(object obj, string name, object value)
        {
            var pi = obj.GetType().GetProperty(name, Any) ?? throw new MissingMemberException(obj.GetType().FullName, name);
            if (value is Il2CppObjectBase ob && !pi.PropertyType.IsInstanceOfType(ob)) value = CastTo(ob, pi.PropertyType);
            pi.SetValue(obj, value);
        }

        static readonly Dictionary<(Type, string), MethodInfo> _methods = new();

        static object Call(object obj, string name, params object[] args)
        {
            var t = obj.GetType();
            if (!_methods.TryGetValue((t, name), out var m))
            {
                m = t.GetMethods(Any).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length)
                    ?? throw new MissingMethodException(t.FullName, name);
                _methods[(t, name)] = m;
            }
            // Il2Cpp wrappers must be passed as the exact parameter type the method declares.
            var ps = m.GetParameters();
            for (int i = 0; i < args.Length; i++)
                if (args[i] is Il2CppObjectBase ob && !ps[i].ParameterType.IsInstanceOfType(ob))
                    args[i] = CastTo(ob, ps[i].ParameterType);
            return m.Invoke(obj, args);
        }

        static object TryCastTo(Il2CppObjectBase o, Type t) =>
            typeof(Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(t).Invoke(o, null);

        static object CastTo(Il2CppObjectBase o, Type t) =>
            typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(t).Invoke(o, null);

        static float Dist((float x, float y, float z) a, (float x, float y, float z) b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static bool IsAlive(object o)
        {
            try { return Get(o, "transform") != null; } catch { return false; }
        }

        void Rescan()
        {
            _bows.Clear(); _hands.Clear(); _loaderByBow.Clear();
            _bows.AddRange(FindAll(_tBow));
            _hands.AddRange(FindAll(_tHand));
            if (_tLoader != null) foreach (var l in FindAll(_tLoader)) AddLoader(l);
            LogCounts("scan");
        }

        // Objects that started since the last frame (queued by the Start postfixes).
        void DrainRegistrations()
        {
            if (Registry.Pending.Count == 0) return;
            foreach (var o in Registry.Pending)
            {
                try
                {
                    if (_tLoader != null && _tLoader.IsInstanceOfType(o))
                    {
                        var b = Get(o, "bow");
                        if (b != null && !Contains(_bows, b)) _bows.Add(b);
                        AddLoader(o);
                    }
                    else if (_tHand.IsInstanceOfType(o) && !Contains(_hands, o)) _hands.Add(o);
                }
                catch { }
            }
            Registry.Pending.Clear();
            LogCounts("registered");
        }

        void AddLoader(object l)
        {
            try
            {
                var b = Get(l, "bow");
                if (b != null) _loaderByBow[((Il2CppObjectBase)b).Pointer] = l;
            }
            catch { }
        }

        static bool Contains(List<object> list, object o)
        {
            IntPtr p = ((Il2CppObjectBase)o).Pointer;
            foreach (var x in list) if (((Il2CppObjectBase)x).Pointer == p) return true;
            return false;
        }

        void LogCounts(string what)
        {
            var summary = $"{what}: {_bows.Count} bow(s), {_hands.Count} hand(s), {_loaderByBow.Count} arrow loader(s)";
            if (_debug.Value && summary != _lastScan) LoggerInstance.Msg(summary);
            _lastScan = summary;
        }

        IEnumerable<object> FindAll(Type t)
        {
            var il2Type = _il2cppTypeFrom.Invoke(null, new object[] { t });
            var arr = (IEnumerable)_findObjectsOfType.Invoke(null, new[] { il2Type });
            var cast = _castGeneric.MakeGenericMethod(t);
            var list = new List<object>();
            foreach (var o in arr)
                if (o != null) list.Add(cast.Invoke(o, null));
            return list;
        }

        bool ResolveTypes()
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            Type Find(string full) =>
                asms.Select(a => a.GetType("Il2Cpp" + full, false) ?? a.GetType(full, false)).FirstOrDefault(x => x != null);

            _tBow = Find("HurricaneVR.Framework.Weapons.Bow.HVRBowBase");
            _tHand = Find("HurricaneVR.Framework.Core.Grabbers.HVRHandGrabber");
            _tLoader = Find("HurricaneVR.Framework.Weapons.Bow.HVRArrowLoader");
            _tUnityObject = asms.Where(a => a.GetName().Name == "UnityEngine.CoreModule")
                                .Select(a => a.GetType("UnityEngine.Object", false)).FirstOrDefault(x => x != null);

            if (_tBow == null || _tHand == null || _tUnityObject == null)
            {
                // Interop assemblies may not be loaded yet on the very first frames; retry a few times.
                if (_frame > 600)
                {
                    _typesFailed = true;
                    LoggerInstance.Error($"could not find game types (bow={_tBow != null}, hand={_tHand != null}, unity={_tUnityObject != null}) - mod disabled.");
                }
                return false;
            }

            _il2cppTypeFrom = typeof(Il2CppInterop.Runtime.Il2CppType).GetMethod("From", new[] { typeof(Type) });
            _findObjectsOfType = _tUnityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "FindObjectsOfType" && !m.IsGenericMethod && m.GetParameters().Length == 1);
            _castGeneric = typeof(Il2CppObjectBase).GetMethod("Cast");
            _typesReady = true;
            LoggerInstance.Msg($"hooked: {_tBow.FullName}, {_tHand.FullName}, loader={_tLoader != null}");
            return true;
        }
    }

    // ---- registration: objects announce themselves on Start instead of being searched for ----
    internal static class Registry
    {
        internal static readonly List<object> Pending = new();
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppHurricaneVR.Framework.Weapons.Bow.HVRArrowLoader), "Start")]
    internal static class LoaderStartPatch
    {
        static void Postfix(Il2CppHurricaneVR.Framework.Weapons.Bow.HVRArrowLoader __instance)
        {
            if (__instance != null) Registry.Pending.Add(__instance);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppHurricaneVR.Framework.Core.Grabbers.HVRHandGrabber), "Start")]
    internal static class HandStartPatch
    {
        static void Postfix(Il2CppHurricaneVR.Framework.Core.Grabbers.HVRHandGrabber __instance)
        {
            if (__instance != null) Registry.Pending.Add(__instance);
        }
    }

    // ---- Fix 3: one arrow detonates an explosive barrel --------------------------------
    // ANBBreakable.hit(origin, bulletDamage, isBullet, distance, isExplosion, fromEnemy) is called
    // synchronously from inside ANBKnife.stabEnemy, so a flag set around stabEnemy tells hit()
    // that the current damage comes from an arrow. Only explosive breakables are touched;
    // knives, bullets, explosions and non-explosive breakables keep the game's own numbers.
    internal static class ArrowStab
    {
        internal static bool Active;
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ANBKnife), nameof(Il2Cpp.ANBKnife.stabEnemy))]
    internal static class StabEnemyPatch
    {
        static void Prefix(Il2Cpp.ANBKnife __instance)
        {
            try { ArrowStab.Active = __instance != null && __instance.isArrow; }
            catch { ArrowStab.Active = false; }
        }

        static void Postfix() => ArrowStab.Active = false;
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ANBBreakable), nameof(Il2Cpp.ANBBreakable.hit))]
    internal static class BreakableHitPatch
    {
        static void Prefix(Il2Cpp.ANBBreakable __instance, ref float bulletDamage, bool isBullet, bool isExplosion)
        {
            if (!ArrowStab.Active || isBullet || isExplosion) return;
            if (ArrowGrabAssistMod.ExplosiveArrows == null || !ArrowGrabAssistMod.ExplosiveArrows.Value) return;
            try
            {
                if (!__instance.explosionOnBreak || __instance.indistructable) return;
                // hitAfterTime does health -= damage and breaks at health <= 0.
                float needed = __instance.health + 1f;
                if (bulletDamage >= needed) return;
                if (ArrowGrabAssistMod.DebugLogEntry != null && ArrowGrabAssistMod.DebugLogEntry.Value)
                    ArrowGrabAssistMod.Log.Msg($"arrow hit explosive barrel: damage {bulletDamage:0.#} -> {needed:0.#} (health {__instance.health:0.#})");
                bulletDamage = needed;
            }
            catch (Exception e)
            {
                ArrowGrabAssistMod.Log?.Warning($"barrel patch skipped: {e.Message}");
            }
        }
    }
}
