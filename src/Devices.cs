// Known G-Wolves models (vendor ID 0x33E4).
// Taken from the public device list the official web configurator (mouse.xyz) loads in the browser
// (static/json/env-models-*.json): wired PID, wireless/receiver PID(s) and protocol per model.
// Names are shortened: no "8K"/"4K", "HSK Pro 2.0" -> "HSK Pro V2", "Fenir" typo fixed.

using System;
using System.Collections.Generic;

namespace MouseBattery
{
    public enum Protocol { Feature, Legacy, Compx }

    public class Model
    {
        public string Name;
        public Protocol Protocol;
        public bool Wired;
        public Model(string name, Protocol p, bool wired) { Name = name; Protocol = p; Wired = wired; }
    }

    public static class Devices
    {
        public const ushort VendorId = 0x33E4;

        // Receiver shared by many newer models ("Receiver RS"). The model is asked for with a read-only query (model ID).
        public const ushort SharedReceiverPid = 0x3854;

        public static readonly Dictionary<ushort, Model> ByPid = new();
        public static readonly Dictionary<int, string> SharedReceiverModels = new()
        {
            [2] = "HTS Plus", [4] = "Lycan", [5] = "HTXU", [6] = "Lycan", [7] = "HTXU", [8] = "HTS Plus",
            [9] = "Fenrir Pro", [10] = "HTX Mini", [11] = "HTS Plus Pro", [12] = "WARG", [13] = "HTX Mini Asym",
            [15] = "VUK Mini", [17] = "Fenrir Asym", [21] = "Fenrir Max Asym",
        };

        static void Add(string name, Protocol p, ushort wired, params ushort[] wireless)
        {
            ByPid[wired] = new Model(name, p, true);
            foreach (var w in wireless) ByPid[w] = new Model(name, p, false);
        }

        static Devices()
        {
            // Newer "Feature" protocol (command 0x83)
            Add("HTM Plus", Protocol.Feature, 0x3808, 0x3817);
            Add("HSK Pro V2", Protocol.Feature, 0x6808, 0x6817);
            Add("HTXU", Protocol.Feature, 0x5608, 0x5617);
            Add("Fenrir Pro", Protocol.Feature, 0x3608, 0x3617);

            // Older "Legacy" protocol (command 0x8F); some have a 4K and an 8K receiver
            Add("VUK", Protocol.Legacy, 0x3908, 0x3917);
            Add("HT-S2", Protocol.Legacy, 0x7904, 0x7913);
            Add("HT-S2 Pro", Protocol.Legacy, 0x7908, 0x7917);
            Add("Fenrir", Protocol.Legacy, 0x3508, 0x3517);
            Add("Fenrir Max", Protocol.Legacy, 0x3708, 0x3717);
            Add("HTS Ultra", Protocol.Legacy, 0x5308, 0x5317);
            Add("HTR", Protocol.Legacy, 0x7704, 0x7713);
            Add("HTR Pro", Protocol.Legacy, 0x7708, 0x7717);
            Add("HSK Pro", Protocol.Legacy, 0x5808, 0x5817, 0x5807);
            Add("HSK Plus", Protocol.Legacy, 0x5908, 0x5917, 0x5907);
            Add("HSK Lite", Protocol.Legacy, 0x7204, 0x7203);
            Add("HTS Plus", Protocol.Legacy, 0x5408, 0x5417, 0x5407);
            Add("HTX", Protocol.Legacy, 0x5708, 0x5717, 0x5707);
            Add("HTX Mini", Protocol.Legacy, 0x2708, 0x2717);
            Add("HSK Pro ACE", Protocol.Legacy, 0x5804, 0x5803);
            Add("HSK Plus ACE", Protocol.Legacy, 0x5904, 0x5903);
            Add("HTS Plus ACE", Protocol.Legacy, 0x5404, 0x5403);
            Add("HTX ACE", Protocol.Legacy, 0x5704, 0x5703);

            // "Compx" protocol: wired PIDs; wireless they all use the shared receiver 0x3854
            Add("HTS Plus", Protocol.Compx, 0x5418);
            Add("HTS Plus", Protocol.Compx, 0x5419);
            Add("HTS Plus Pro", Protocol.Compx, 0x5219);
            Add("HTXU", Protocol.Compx, 0x5618);
            Add("HTXU", Protocol.Compx, 0x5619);
            Add("HTX Mini", Protocol.Compx, 0x2719);
            Add("HTX Mini Asym", Protocol.Compx, 0x2619);
            Add("Lycan", Protocol.Compx, 0x4718);
            Add("Lycan", Protocol.Compx, 0x4719);
            Add("Fenrir Pro", Protocol.Compx, 0x3619);
            Add("Fenrir Asym", Protocol.Compx, 0x3519);
            Add("Fenrir Max Asym", Protocol.Compx, 0x1619);
            Add("WARG", Protocol.Compx, 0x4219);
            Add("VUK Mini", Protocol.Compx, 0x2119);
            ByPid[SharedReceiverPid] = new Model(null, Protocol.Compx, false);
        }
    }
}
