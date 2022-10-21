﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using GameMod.VersionHandling;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod {
    public class MethodProfile
    {
        public MethodBase method = null;
        public string overrideName = null;
        public int overrideHash = 0;
        public ulong count = 0;
        public long ticksTotal;
        public long ticksMin;
        public long ticksMax;
        public long ticksStart;
        public int depth = 0;

        public MethodProfile()
        {
            method = null;
            overrideName = null;
            count = 0;
            ticksTotal = 0;
            ticksMin = 0;
            ticksMax = 0;
            depth = 0;
        }

        public MethodProfile(MethodBase mb)
        {
            method = mb;
            Reset();
        }

        public MethodProfile(string ovName, int ovHash)
        {
            method = null;
            overrideName = ovName;
            overrideHash = ovHash;
            Reset();
        }

        public void Reset()
        {
            count = 0;
            ticksTotal = 0;
            ticksMin = 0;
            ticksMax = 0;
            depth = 0;
        }

        public MethodProfile Start()
        {
            //UnityEngine.Debug.LogFormat("Prefix called {0}", method );
            if (depth != 0) {
                return null;
            }
            depth = 1;
            ticksStart = PoorMansProfiler.timerBase.ElapsedTicks;
            return this;
        }

        public void End()
        {
            long ticks = PoorMansProfiler.timerBase.ElapsedTicks - ticksStart;
            if (count == 0) {
                ticksMin = ticks;
                ticksMax = ticks;
            } else {
                if (ticks < ticksMin) {
                    ticksMin = ticks;
                } else if (ticks > ticksMax) {
                    ticksMax = ticks;
                }
            }
            ticksTotal += ticks;
            count++;
            depth = 0;
            //UnityEngine.Debug.LogFormat("Postfix called {0} {1} {2} {3}", method, count, ticksTotal, ticksTotal/(double)count);
        }

        public void ImportTicks(long ticks)
        {
            if (count == 0) {
                ticksMin = ticks;
                ticksMax = ticks;
            } else {
                if (ticks < ticksMin) {
                    ticksMin = ticks;
                } else if (ticks > ticksMax) {
                    ticksMax = ticks;
                }
            }
            ticksTotal += ticks;
            count++;
        }

        public void ImportFrametime(float f)
        {
            long ticks = (long)(f * Stopwatch.Frequency);
            ImportTicks(ticks);
        }

        public enum Info {
            Name,
            AvgTime,
            TotalTime,
            MinTime,
            MaxTime,
            Count
        }

        public int GetHash()
        {
            if (String.IsNullOrEmpty(overrideName)) {
                return method.GetHashCode();
            }
            return overrideHash;
        }

        public double GetValueD(Info inf)
        {
            double cnt = (count > 0)?(double)count:1.0;
            double res = -1.0;
            switch(inf) {
                case Info.AvgTime:
                    res = ((double)ticksTotal * PoorMansProfiler.timerBaseToMS)/cnt;
                    break;
                case Info.TotalTime:
                    res = ((double)ticksTotal * PoorMansProfiler.timerBaseToMS);
                    break;
                case Info.MinTime:
                    res = ((double)ticksMin * PoorMansProfiler.timerBaseToMS);
                    break;
                case Info.MaxTime:
                    res = ((double)ticksMax * PoorMansProfiler.timerBaseToMS);
                    break;
                case Info.Count:
                    res = count;
                    break;
            }
            return res;
        }

        public string GetInfo(Info inf)
        {
            string result;

            if (inf == Info.Name) {
                if (String.IsNullOrEmpty(overrideName)) {
                    result = method.DeclaringType.FullName + " " + method.ToString();
                } else {
                    result = overrideName;
                }
            } else if (inf == Info.Count) {
                result = count.ToString();
            } else {
                double val = GetValueD(inf);
                result = val.ToString();
            }
            return result;
        }

        public void WriteResults(StreamWriter sw)
        {
            sw.Write("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\n", GetValueD(Info.AvgTime), GetInfo(Info.Count), GetValueD(Info.TotalTime), GetValueD(Info.MinTime), GetValueD(Info.MaxTime), GetInfo(Info.Name));
        }
    }


    public class MethodProfileCollector
    {
        public const int MaxEntryCount = 5000;
        public MethodProfile[] entry;

        public MethodProfileCollector() {
            entry = new MethodProfile[MaxEntryCount+1];
        }
    }

    public class PoorMansFilter
    {
        public enum Operation {
            None,
            Include,
            Exclude,
        }

        public enum Select {
            Exact,
            Contains,
            RegEx,
            Always,
        }

        public enum Mode {
            All,
            PreviouslyPatched,
        }

        public enum Flags : uint {
            ShortTypeName = 0x1,
        }

        public Operation op;
        public Select sel;
        public Mode   mode;
        public string typeFilter;
        public string methodFilter;
        public uint flags;

        public PoorMansFilter(Operation o, Select s, Mode m, uint f, string typeF, string methodF)
        {
            Set(o,s,m,f,typeF,methodF);
        }

        public PoorMansFilter(String lineDesc)
        {
            if (!Set(lineDesc)) {
                Set(Operation.None, Select.Contains, Mode.All, 0, null, null);
            }
        }

        public void Set(Operation o, Select s, Mode m, uint f, string typeF, string methodF)
        {
            op = o;
            sel = s;
            mode = m;
            flags = f;
            typeFilter = typeF;
            methodFilter = methodF;
        }

        public bool Set(string lineDesc)
        {
            Operation o = Operation.Include;
            Select s = Select.Contains;
            Mode m = Mode.All;
            uint f = 0;
            string typeF = null;
            string methodF = null;

            if (!String.IsNullOrEmpty(lineDesc)) {
                string[] parts = lineDesc.Split('\t');
                if (parts.Length < 2) {
                    methodF = lineDesc;
                }  else {
                    o = GetOp(parts[0]);
                    s = GetSel(parts[0]);
                    m = GetMode(parts[0]);
                    f = GetFlags(parts[0]);
                    if (parts.Length > 2) {
                        typeF = parts[1];
                        methodF = parts[2];
                    } else {
                        methodF = parts[1];
                    }
                }
                Set(o,s,m,f,typeF,methodF);
                return true;
            }
            return false;
        }

        private static Operation GetOp(string opts)
        {
            Operation o = Operation.Include;

            for (var i=0; i<opts.Length; i++) {
                switch(opts[i]) {
                    case '+':
                        o = Operation.Include;
                        break;
                    case '-':
                        o = Operation.Exclude;
                        break;
                    case 'N':
                        o = Operation.None;
                        break;
                }
            }
            return o;
        }

        private static Select GetSel(string opts)
        {
            Select s = Select.Contains;

            for (var i=0; i<opts.Length; i++) {
                switch(opts[i]) {
                    case '=':
                        s = Select.Exact;
                        break;
                    case 'R':
                        s = Select.RegEx;
                        break;
                    case 'C':
                        s = Select.Contains;
                        break;
                    case '*':
                        s = Select.Always;
                        break;
                }
            }
            return s;
        }

        private static Mode GetMode(string opts)
        {
            Mode m = Mode.All;

            for (var i=0; i<opts.Length; i++) {
                switch(opts[i]) {
                    case 'a':
                        m = Mode.All;
                        break;
                    case 'p':
                        m = Mode.PreviouslyPatched;
                        break;
                }
            }
            return m;
        }

        private static uint GetFlags(string opts)
        {
            uint f = 0;

            for (var i=0; i<opts.Length; i++) {
                switch(opts[i]) {
                    case '_':
                        f |= (uint)Flags.ShortTypeName;
                        break;
                }
            }
            return f;
        }

        public void Write(StreamWriter sw)
        {
            string o;
            string s;
            string m;
            string f="";

            switch (op) {
                case Operation.Include:
                    o = "+";
                    break;
                case Operation.Exclude:
                    o = "-";
                    break;
                default:
                    o = "N";
                    break;
            }

            switch(sel) {
                case Select.Exact:
                    s = "=";
                    break;
                case Select.RegEx:
                    s = "R";
                    break;
                case Select.Always:
                    s = "*";
                    break;
                default:
                    s = "C";
                    break;
            }

            switch (mode) {
                case Mode.PreviouslyPatched:
                    m = "p";
                    break;
                default:
                    m = "a";
                    break;
            }

            if ( (flags & (uint)Flags.ShortTypeName) != 0) {
                f += "_";
            }
            sw.Write("{0}{1}{2}\t{3}\t{4}\n",o,s,m,f,typeFilter, methodFilter);
        }

        public Operation Apply(MethodBase m, bool isPreviouslyPatched)
        {
            if (op == Operation.None) {
                return op;
            }

            if (mode == Mode.PreviouslyPatched && !isPreviouslyPatched) {
                return Operation.None;
            }

            string tname = ((flags & (uint)Flags.ShortTypeName) != 0)?m.DeclaringType.Name:m.DeclaringType.FullName;
            if (Matches(tname, typeFilter) && Matches(m.ToString(), methodFilter)) {
                return op;
            }
            return Operation.None;
        }

        private bool Matches(string str, string filter)
        {
            if (String.IsNullOrEmpty(filter)) {
                return true;
            }
            switch (sel) {
                case Select.Exact:
                    if (str == filter) {
                        return true;
                    }
                    break;
                case Select.Contains:
                    if (str.IndexOf(filter)>=0) {
                        return true;
                    }
                    break;
                case Select.RegEx:
                    Regex rgx = new Regex(filter);
                    return rgx.IsMatch(str);
                case Select.Always:
                    return true;
            }
            return false;
        }
    }

    public class PoorMansFilterList
    {
        public List<PoorMansFilter> filters = new List<PoorMansFilter>();

        public bool Load(string filename)
        {
            if (File.Exists(filename)) {
                StreamReader sr = new StreamReader(filename, new System.Text.UTF8Encoding());
                string line;
                int cnt = 0;
                while( (line = sr.ReadLine()) != null) {
                    if (line[0] != '#') {
                        PoorMansFilter f = new PoorMansFilter(line);
                        if (f.op != PoorMansFilter.Operation.None) {
                          Add(f);
                          cnt ++;
                        }
                    }
                }
                sr.Dispose();
                UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: added {0} filters from list {1}", cnt, filename);
                return true;
            }
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: can't find filter list file {0}", filename);
            return false;
        }

        public void Save(string filename)
        {
            var sw = new StreamWriter(filename, false);
            sw.Write("# POOR MAN's PROFILER Filter File v1\n");
            foreach (var f in filters) {
                f.Write(sw);
            }

            sw.Dispose();
        }

        public void Add(PoorMansFilter f)
        {
            filters.Add(f);
        }

        public void Add(MethodBase m, bool isPreviouslyPatched)
        {
            Add (new PoorMansFilter(PoorMansFilter.Operation.Include,
                                    PoorMansFilter.Select.Exact,
                                    (isPreviouslyPatched)?PoorMansFilter.Mode.PreviouslyPatched:PoorMansFilter.Mode.All,
                                    0,
                                    m.DeclaringType.FullName,
                                    m.ToString()));
        }

        public void AddDefaults()
        {
            // Add all previously patched Methods
            Add (new PoorMansFilter(PoorMansFilter.Operation.Include,
                                    PoorMansFilter.Select.Always,
                                    PoorMansFilter.Mode.PreviouslyPatched,
                                    0,
                                    null,
                                    null));
        }

        public bool Apply(MethodBase m, bool isPreviouslyPatched)
        {
            foreach(var f in filters) {
                PoorMansFilter.Operation op = f.Apply(m, isPreviouslyPatched);
                if (op == PoorMansFilter.Operation.Include) {
                    return true;
                }
                if (op == PoorMansFilter.Operation.Exclude) {
                    return false;
                }
            }
            return false;
        }
    }


    public class PoorMansProfiler
    {
        private static Dictionary<MethodBase,MethodProfile> profileData = new Dictionary<MethodBase, MethodProfile>();
        private static Dictionary<MethodBase,MethodProfile>[] intervalData = new Dictionary<MethodBase, MethodProfile>[MethodProfileCollector.MaxEntryCount];

        public static Stopwatch timerBase = new Stopwatch();
        public static  double timerBaseToMS = -1.0;
        private static long intervalStart = 0;
        private static long intervalEnd = 0;

        private static int curIdx = 0;
        private static int curFixedTick = 0;
        private static DateTime startTime = DateTime.UtcNow;
        private static int fixedTickCount = 60; // 1 second interval by default (during MP at least)
        private static long cycleLongIntervals = 60000; // >= 60 seconds long intervals force a full cycle

        private static MethodInfo pmpFrametimeDummy = AccessTools.Method(typeof(PoorMansProfiler),"PoorMansFrametimeDummy");
        private static MethodInfo pmpIntervalTimeDummy = AccessTools.Method(typeof(PoorMansProfiler),"PoorMansIntervalTimeDummy");

        public static bool LooksLikeMessageHander(MethodInfo m)
        {
            if (m != null && !String.IsNullOrEmpty(m.Name)) {
                var p = m.GetParameters();
                if (p.Length == 1 && (p[0].ParameterType.Name == "NetworkMessage")) {
                    if (m.Name.Length > 3 && m.Name[0] == 'O' && m.Name[1] == 'n' &&
                        m.Name != "OnSerialize" && m.Name != "OnDeserialize" && m.Name != "OnNetworkDestroy") {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool LooksLikeUpdateFunc(MethodInfo m)
        {
            if (m != null && !String.IsNullOrEmpty(m.Name) && m.Name == "Update") {
                var p = m.GetParameters();
                if (p.Length == 0 && m.ReturnType == typeof(void)) {
                    // ignore uninteresting or problematic functions...
                    if (m.DeclaringType.FullName.IndexOf("Rewired.") < 0 &&
                        m.DeclaringType.FullName.IndexOf("Smooth.") < 0 && 
                        m.DeclaringType.FullName.IndexOf("Window") < 0 && 
                        m.DeclaringType.FullName.IndexOf("Xbox") < 0 && 
                        m.DeclaringType.FullName.IndexOf("DonetwoSimpleCamera") < 0 && 
                        m.DeclaringType.FullName.IndexOf("SteamManager") < 0 && 
                        m.DeclaringType.FullName.IndexOf("uConsole") < 0 &&
                        m.DeclaringType.FullName.IndexOf("TrackIRComponent") < 0 &&
                        m.DeclaringType.FullName.IndexOf("Overload.SFXCueManager") < 0 &&
                        m.DeclaringType.FullName.IndexOf("UnityEngine.") < 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool LooksLikeInterestingFunc(MethodInfo m)
        {
            if (m  == null || String.IsNullOrEmpty(m.Name)) {
                return false;
            }

            if (m.DeclaringType.FullName.IndexOf("Physics") >= 0) {
                if (m.Name.IndexOf("SphereCast") == 0 ||
                    m.Name.IndexOf("Linecast") == 0) {
                    return true;
                }
            }
            return false;
        }

        // Initialize and activate the Profiler via harmony
        public static void Initialize(Harmony harmony)
        {
            string intervalLength;
            if (GameMod.Core.GameMod.FindArgVal("-pmp-interval", out intervalLength) && !String.IsNullOrEmpty(intervalLength)) {
                fixedTickCount = Int32.Parse(intervalLength);
            }
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: enabled, using intervals of {0} tixed Ticks", fixedTickCount);

            // Dictionary of all previously patched methods
            Dictionary<MethodBase,bool> patchedMethods = new Dictionary<MethodBase,bool>();
            // List of all methods we want to profile
            Dictionary<MethodBase,bool> targetMethods = new Dictionary<MethodBase,bool>();

            // Get the list of all fiters
            string filterFileArg = null;
            PoorMansFilterList filters = new PoorMansFilterList();
            if (GameMod.Core.GameMod.FindArgVal("-pmp-filter", out filterFileArg) && !String.IsNullOrEmpty(filterFileArg)) {
                foreach (var f in filterFileArg.Split(';',',',':')) {
                    filters.Load(Path.Combine(Application.persistentDataPath, f));
                }
            } else {
                filters.Load(Path.Combine(Application.persistentDataPath, "pmp-filters.txt"));
            }
            if (filters.filters.Count < 1) {
                filters.AddDefaults();
                UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: using default filters");
            }
            //filters.Save("/tmp/pmpa");
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: using filter with {0} entries", filters.filters.Count);

            // apply to the previously patched methods
            foreach(var m in harmony.GetPatchedMethods()) {
                patchedMethods[m] = true;
                if (filters.Apply(m,true)) {
                    targetMethods[m] = true;
                    UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: selected {0} {1} (previously patched)", m.DeclaringType.FullName, m.ToString());
                }
            }
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: found {0} previously patched methods", patchedMethods.Count);


            Assembly ourAsm = Assembly.GetExecutingAssembly();
            Assembly overloadAsm = Assembly.GetAssembly(typeof(Overload.GameManager));
            Assembly unityAsm = Assembly.GetAssembly(typeof(Physics));

            Assembly[] assemblies=new Assembly[]{ourAsm, overloadAsm, unityAsm};
            foreach (var asm in assemblies) {
                foreach (var t in asm.GetTypes()) {
                    foreach(var m in t.GetMethods(AccessTools.all)) {
                        //UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: XXX {0} {1}", m.DeclaringType.FullName, m.ToString());
                        if (!patchedMethods.ContainsKey(m) && filters.Apply(m, false)) {
                            targetMethods[m] = true;
                            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: selected {0} {1}", m.DeclaringType.FullName, m.ToString());
                        }
                    }
                }
            }
            
            // Patch the methods with the profiler prefix and postfix
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: applying to {0} methods", targetMethods.Count);
            MethodInfo mPrefix = typeof(PoorMansProfiler).GetMethod("PoorMansProfilerPrefix");
            MethodInfo mPostfix = typeof(PoorMansProfiler).GetMethod("PoorMansProfilerPostfix");
            var hmPrefix = new HarmonyMethod(mPrefix, Priority.First);
            var hmPostfix = new HarmonyMethod(mPostfix, Priority.Last);
            foreach (KeyValuePair<MethodBase,bool> pair in targetMethods) {
                if (pair.Value) {
                    harmony.Patch(pair.Key, hmPrefix, hmPostfix);
                }
            }

            // Additional Patches for management of the Profiler itself
            harmony.Patch(AccessTools.Method(typeof(GameManager), "Start"), null, new HarmonyMethod(typeof(PoorMansProfiler).GetMethod("StartPostfix"), Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(Overload.Client), "OnMatchEnd"), null, new HarmonyMethod(typeof(PoorMansProfiler).GetMethod("MatchEndPostfix"), Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(Overload.Client), "OnStartPregameCountdown"), null, new HarmonyMethod(typeof(PoorMansProfiler).GetMethod("StartPregamePostfix"), Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(Overload.GameManager), "FixedUpdate"), null, new HarmonyMethod(typeof(PoorMansProfiler).GetMethod("FixedUpdatePostfix"), Priority.Last));
            harmony.Patch(AccessTools.Method(typeof(Overload.GameManager), "Update"), null, new HarmonyMethod(typeof(PoorMansProfiler).GetMethod("UpdatePostfix"), Priority.Last));

            startTime = DateTime.UtcNow;
            timerBaseToMS = 1000.0 / (double)Stopwatch.Frequency;
            timerBase.Reset();
            timerBase.Start();
            intervalStart = timerBase.ElapsedTicks;
        }

        // The Prefix run at the start of every target method
        public static void PoorMansProfilerPrefix(MethodBase __originalMethod, out MethodProfile __state)
        {
            MethodProfile mp;
            try {
                mp = profileData[__originalMethod];
            } catch (KeyNotFoundException) {
                mp = new MethodProfile(__originalMethod);
                profileData[__originalMethod] = mp;
            }
            __state = mp.Start();
        }

        // The Postfix run at the end of every target method
        public static void PoorMansProfilerPostfix(MethodProfile __state)
        {
            if (__state != null) {
                __state.End();
            }
        }

        // This is an additional Postfix to GameManager.Start() to registe our console commands
        public static void StartPostfix()
        {
            uConsole.RegisterCommand("pmpcycle", "Cycle Poor Man's Profiler data", CmdCycle);
        }

        // This is an additional Postfix to Overload.Client.OnMatchEnd() to cycle the profiler data
        public static void MatchEndPostfix()
        {
            Cycle("match");
        }

        // This is an additional Postfix to Overload.Client.OnStartPregameCountdown() to cycle the profiler data
        public static void StartPregamePostfix()
        {
            Cycle("pregame");
        }

        // This is an additional Postfix to Overload.GameManager.FixedUpdate() to cycle the internal profiler data
        public static void FixedUpdatePostfix()
        {
            if (cycleLongIntervals > 0 && (timerBase.ElapsedMilliseconds - intervalStart > cycleLongIntervals )) {
                Cycle("long");
                return;
            }
            if (++curFixedTick >= fixedTickCount) {
                CycleInterval();
                curFixedTick = 0;
            }
        }

        // This is an additional Postfix to Overload.GameManager.Update() to gather frame statistics
        public static void UpdatePostfix()
        {
            MethodProfile mp;
            try {
                mp = profileData[pmpFrametimeDummy];
            } catch (KeyNotFoundException) {
                mp = new MethodProfile("+++PMP-Frametime",-7777);
                profileData[pmpFrametimeDummy] = mp;
            }
            mp.ImportFrametime(Time.unscaledDeltaTime);
        }

        // This is a dummy method only used for FPS statistic as key in the Dictionaries...
        public static void PoorMansFrametimeDummy()
        {
        }

        // This is a dummy method only used for FPS statistic as key in the Dictionaries...
        public static void PoorMansIntervalTimeDummy()
        {
        }

        // Collect the current profile Data into the collector
        public static void Collect(Dictionary<MethodBase,MethodProfileCollector> pdc, Dictionary<MethodBase,MethodProfile> data, int idx)
        {
            if (idx > MethodProfileCollector.MaxEntryCount) {
                return;
            }

            if (pdc.Count < 1) {
                foreach( KeyValuePair<MethodBase,MethodProfile> pair in data) {
                    MethodProfileCollector coll = new MethodProfileCollector();
                    coll.entry[MethodProfileCollector.MaxEntryCount] = pair.Value;
                    coll.entry[idx] = pair.Value;
                    pdc[pair.Key] = coll;
                }
            } else {
                foreach( KeyValuePair<MethodBase,MethodProfile> pair in data) {
                    MethodProfileCollector coll = null;
                    try {
                        coll = pdc[pair.Key];
                    } catch (KeyNotFoundException) {
                        coll = new MethodProfileCollector();
                        coll.entry[MethodProfileCollector.MaxEntryCount] = pair.Value;
                        pdc[pair.Key] = coll;
                    }
                    coll.entry[idx] = pair.Value;
                }
            }
        }

        // Console command pmpcycle
        static void CmdCycle()
        {
            Cycle("manual");
        }

        public static string GetTimestamp(DateTime ts)
        {
			return ts.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Function to write the statistics to a file
        public static void WriteResults(Dictionary<MethodBase,MethodProfile> data, string filename, string timestamp)
        {
            var sw = new StreamWriter(filename, false);
            sw.Write("+++ OLMOD - Poor Man's Profiler v1\n");
            sw.Write("+++ run at {0}\n",timestamp);
            foreach( KeyValuePair<MethodBase,MethodProfile> pair in data) {
                //UnityEngine.Debug.LogFormat("XXX {0}", pair.Value.method);
                pair.Value.WriteResults(sw);
            }
            sw.Write("+++ Dump ends here\n");
            sw.Dispose();
        }


        // Function to write the statistics info file
        public static void WriteResultsInfo(Dictionary<MethodBase,MethodProfileCollector> pdc, string baseFilename, int cnt, DateTime tsBegin, DateTime tsEnd)
        {
            var sw = new StreamWriter(baseFilename + "info.csv", false);
            sw.Write("+++ OLMOD - Poor Man's Profiler v1\n");
            sw.Write("+++ run {0} to {1}, {2} intervals, {3} methods\n",GetTimestamp(tsBegin), GetTimestamp(tsEnd), cnt, pdc.Count);

            int idx = 0;
            foreach( KeyValuePair<MethodBase,MethodProfileCollector> pair in pdc) {
                MethodProfile lmp = pair.Value.entry[MethodProfileCollector.MaxEntryCount];
                sw.Write("{0}\t{1}\t{2}\n",idx, lmp.GetHash(),lmp.GetInfo(MethodProfile.Info.Name));
                idx++;
            }
            sw.Dispose();
        }


        // Function to write one result channel
        public static void WriteResultsValue(Dictionary<MethodBase,MethodProfileCollector> pdc, string baseFilename, int cnt, MethodProfile.Info inf)
        {
            MethodProfile dummy = new MethodProfile();
            dummy.Reset();

            var sw = new StreamWriter(baseFilename + inf.ToString() + ".csv", false);
            foreach( KeyValuePair<MethodBase,MethodProfileCollector> pair in pdc) {
                MethodProfile lmp = pair.Value.entry[MethodProfileCollector.MaxEntryCount];
                sw.Write("{0}",lmp.GetHash());
                for (int i=0; i<cnt; i++) {
                    MethodProfile mp = pair.Value.entry[i];
                    if (mp == null) {
                        mp = dummy;
                    }
                    sw.Write("\t{0}", mp.GetInfo(inf));
                }
                sw.Write("\t{0}\n",lmp.GetInfo(MethodProfile.Info.Name));
            }
            sw.Dispose();
        }

        // Function to write the statistics to a files
        public static void WriteResults(Dictionary<MethodBase,MethodProfileCollector> pdc, string baseFilename, int cnt, DateTime tsBegin, DateTime tsEnd)
        {
            UnityEngine.Debug.LogFormat("POOR MAN's PROFILER: cycle: {0} intervals, {1} methods to {2}*.csv",cnt,pdc.Count,baseFilename);
            WriteResultsInfo(pdc, baseFilename, cnt, tsBegin, tsEnd);
            WriteResultsValue(pdc, baseFilename, cnt, MethodProfile.Info.Count);
            WriteResultsValue(pdc, baseFilename, cnt, MethodProfile.Info.AvgTime);
            WriteResultsValue(pdc, baseFilename, cnt, MethodProfile.Info.MinTime);
            WriteResultsValue(pdc, baseFilename, cnt, MethodProfile.Info.MaxTime);
            WriteResultsValue(pdc, baseFilename, cnt, MethodProfile.Info.TotalTime);
        }

        // Reset the current interval Data
        public static void ResetInterval()
        {
            // create a new Dict so in-fly operations are still well-defined
            profileData = new Dictionary<MethodBase,MethodProfile>();
            intervalStart = timerBase.ElapsedTicks;
        }

        /*
        public static void CycleInterval() {
            Dictionary<MethodBase,MethodProfile> data = profileData;
			string curDateTime = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string ftemplate = String.Format("olmod_pmp_{0}.csv", curDateTime);
            string fn = Path.Combine(Application.persistentDataPath, ftemplate);
            WriteResults(data, fn, curDateTime);
            ResetInterval();
        }
        */

        // Cycle a single profiler interval
        public static void CycleInterval() {
            intervalEnd = timerBase.ElapsedTicks;
            Dictionary<MethodBase,MethodProfile> data = profileData;
            MethodProfile intervalTime = new MethodProfile("+++PMP-Interval", -7778);
            intervalTime.ImportTicks(intervalEnd - intervalStart);
            data[pmpIntervalTimeDummy] = intervalTime;
            intervalData[curIdx] = data;
            //Collect(profileDataCollector, data, curIdx);
            curIdx++;
            if (curIdx >= MethodProfileCollector.MaxEntryCount) {
                Cycle("flush");
            }
            ResetInterval();
        }

        // Cycle Profile Data Collection: flush to disk and start new
        public static void Cycle(string reason)
        {
            DateTime tsEnd = DateTime.UtcNow;
            Dictionary<MethodBase,MethodProfileCollector> pdc = new Dictionary<MethodBase, MethodProfileCollector>();
            for (int i=0; i<curIdx; i++) {
                Collect(pdc, intervalData[i], i);
            }
            intervalData = new Dictionary<MethodBase, MethodProfile>[MethodProfileCollector.MaxEntryCount];
            string curDateTime = GetTimestamp(tsEnd);
            string ftemplate = String.Format("olmod_pmp_{0}_{1}_", curDateTime, reason);
            string fn = Path.Combine(Application.persistentDataPath, ftemplate);
            WriteResults(pdc, fn, curIdx, startTime, tsEnd);
            startTime = DateTime.UtcNow;
            ResetInterval();
            curIdx = 0;
        }

    }
}
