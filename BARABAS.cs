#region SEHEADER
/*
 * This header is here to natively integrate with Visual Studio without MDK,
 * and my Minifier deletes it if it finds it.
 */

#if DEBUG
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using VRageMath;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using Sandbox.Game.EntityComponents;
using System.Text.RegularExpressions;

namespace SpaceEngineers
{
    public sealed class Program : MyGridProgram
    {
#endif
        #endregion
        /*
         * BARABAS v1.7
         *
         * (Burillo's Automatic Resource Administration for BAses and Ships)
         *
         * Published under "do whatever you want with it" license (aka public domain)
         *
         * Color coding for light group "BARABAS Notify":
         * - Red: storage 98% full
         * - Yellow: storage 75% full
         * - Blue: running out of power
         * - Cyan: ice shortage (low oxygen or hydrogen)
         * - Magenta: refinery or assembler clogged
         * - White: running out of materials
         * - Brown: oxygen leak detected
         * - Pink: unfinished/damaged blocks present
         * - Green: connection to base/ship detected
         *
         * To configure BARABAS, edit custom data in programmable block.
         *
         * To exclude a block from being affected by BARABAS, have its name start with "X".
         *
         * Mandatory requirements:
         * - Storage containers must be present.
         *
         * Optional requirements:
         * - Group of text/LCD panels/beacons/antennas/lights named "BARABAS Notify",
         *   used for notification and status reports.
         * - A group of sensors and connectors named "BARABAS Trash".
         *
         * Trash throw out:
         * - During normal operation, only excess gravel will be thrown out as trash.
         * - During crisis mode, some ore may be thrown out as well.
         * - If you don't want gravel to be thrown out (e.g. if you use concrete mod),
         *   edit configuration accordingly.
         * - If no connectors were found in the "BARABAS Trash" group, an arbitrary
         *   connector will be used for trash throw out.
         * - Sensors in "BARABAS Trash" group are used to stop trash throw out. To use
         *   them, simply set up their detection settings and range.
         * - If no sensors were found in the "BARABAS Trash" group, trash throw out will
         *   not stop until all trash is disposed of.
         *
         * NOTE: if you are using BARABAS from source, you will have to minify the
         *       code before pasting it into the programmable block!
         *
         */

        const string VERSION = "1.7";

        #region CONFIGVARS
        // configuration
        const int OP_MODE_AUTO = 0x0;
        const int OP_MODE_SHIP = 0x1;
        const int OP_MODE_DRILL = 0x2;
        const int OP_MODE_GRINDER = 0x4;
        const int OP_MODE_WELDER = 0x8;
        const int OP_MODE_TUG = 0x10;
        const int OP_MODE_BASE = 0x100;

        int op_mode = OP_MODE_AUTO;
        // power high and low watermarks. yay text limits!
        float pwr_hi_wm = 0;
        float pwr_lo_wm = 0;
        // oxygen high and low watermarks
        float o2_hi_wm = 0;
        float o2_lo_wm = 0;
        // hydrogen high and low watermarks
        float h2_hi_wm = 0;
        float h2_lo_wm = 0;
        // this is a threshold that we'll use to decide when to exit low power crisis mode
        float stored_power_thresh = 0f;
        // multiplier for stored material watermarks
        float material_threshold_multiplier = 1f;
        bool throw_out_gravel = false;
        bool sort_storage = true;
        bool hud_notifications = true;
        float prev_pwr_draw = 0;
        bool refineries_clogged = false;
        bool assemblers_clogged = false;

        bool push_ore_to_base = false;
        bool push_ingots_to_base = false;
        bool push_components_to_base = false;
        bool pull_ore_from_base = false;
        bool pull_ingots_from_base = false;
        bool pull_components_from_base = false;
        bool refuel_oxygen = false;
        bool refuel_hydrogen = false;

        // state variables
        int current_state;
        Action[] states = null;

        // crisis mode levels
        enum CrisisMode
        {
            CRISIS_MODE_NONE = 0,
            CRISIS_MODE_THROW_ORE,
            CRISIS_MODE_LOCKUP,
            CRISIS_MODE_NO_POWER
        };
        CrisisMode crisis_mode;
        bool green_mode = false;
        bool trigger_mode = false;
        float update_period = 1.0f; // default to 1 second
        int update_counter = 0;
        int update_counter_max;

        // config options
        const string CS_OP_MODE = "mode";
        const string CS_POWER_WATERMARKS = "power watermarks";
        const string CS_PUSH_ORE = "push ore to base";
        const string CS_PUSH_INGOTS = "push ingots to base";
        const string CS_PUSH_COMPONENTS = "push components to base";
        const string CS_PULL_ORE = "pull ore from base";
        const string CS_PULL_INGOTS = "pull ingots from base";
        const string CS_PULL_COMPONENTS = "pull components from base";
        const string CS_KEEP_STONE = "keep stone";
        const string CS_KEEP_GRAVEL = "keep gravel";
        const string CS_MATERIAL_THRESHOLD_MULT = "storage multiplier";
        const string CS_SORT_STORAGE = "sort storage";
        const string CS_HUD_NOTIFICATIONS = "HUD notifications";
        const string CS_OXYGEN_WATERMARKS = "oxygen watermarks";
        const string CS_HYDROGEN_WATERMARKS = "hydrogen watermarks";
        const string CS_REFUEL_OXYGEN = "refuel oxygen";
        const string CS_REFUEL_HYDROGEN = "refuel hydrogen";
        const string CS_UPDATE_PERIOD = "update period";
        const string CS_GREEN_MODE = "green mode";

        // ore_volume
        const float VOLUME_ORE = 0.37F;
        const float VOLUME_SCRAP = 0.254F;

        // ore names
        const string CO = "Cobalt";
        const string AU = "Gold";
        const string STONE = "Stone";
        const string ICE = "Ice";
        const string FE = "Iron";
        const string MG = "Magnesium";
        const string NI = "Nickel";
        const string PT = "Platinum";
        const string SI = "Silicon";
        const string AG = "Silver";
        const string U = "Uranium";
        const string SCRAP = "Scrap";

        // status items
        const string STATUS_MATERIAL = "Materials";
        const string STATUS_STORAGE_LOAD = "Total storage load/mass";
        const string STATUS_POWER_STATS = "Power (max/cur/left)";
        const string STATUS_ALERT = "Alerts";
        const string STATUS_CRISIS_MODE = "Crisis mode";
        const string STATUS_OXYHYDRO_LEVEL = "O2/H2";

        const float CHUNK_SIZE = 1000;

        // config options, caseless dictionary
        readonly Dictionary<string, string> config_options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { CS_OP_MODE, "" },
            { CS_HUD_NOTIFICATIONS, "" },
            { CS_POWER_WATERMARKS, "" },
            { CS_OXYGEN_WATERMARKS, "" },
            { CS_HYDROGEN_WATERMARKS, "" },
            { CS_REFUEL_OXYGEN, "" },
            { CS_REFUEL_HYDROGEN, "" },
            { CS_PUSH_ORE, "" },
            { CS_PUSH_INGOTS, "" },
            { CS_PUSH_COMPONENTS, "" },
            { CS_PULL_ORE, "" },
            { CS_PULL_INGOTS, "" },
            { CS_PULL_COMPONENTS, "" },
            { CS_KEEP_GRAVEL, "" },
            { CS_MATERIAL_THRESHOLD_MULT, "" },
            { CS_SORT_STORAGE, "" },
            { CS_UPDATE_PERIOD, "" },
            { CS_GREEN_MODE, "" },
        };

        // status report fields
        readonly Dictionary<string, string> status_report = new Dictionary<string, string> {
            { STATUS_STORAGE_LOAD, "" },
            { STATUS_POWER_STATS, "" },
            { STATUS_OXYHYDRO_LEVEL, "" },
            { STATUS_MATERIAL, "" },
            { STATUS_ALERT, "" },
            { STATUS_CRISIS_MODE, "" },
        };

        readonly List<string> ore_types = new List<string> {
            CO, AU, FE, MG, NI, PT, SI, AG, U, STONE, ICE
        };

        // map block definition to list of ores it can refine
        readonly Dictionary<string, HashSet<string>> block_refine_map = new Dictionary<string, HashSet<string>> {
            { "MyObjectBuilder_Refinery/LargeRefinery", new HashSet<string> {CO, AU, FE, MG, NI, PT, SI, AG, U, STONE, ICE, SCRAP} },
            { "MyObjectBuilder_Refinery/Blast Furnace", new HashSet<string> {CO, FE, NI, SCRAP, STONE} },
            { "MyObjectBuilder_OxygenGenerator/", new HashSet<string> {ICE} },
        };

        // ballpark values of "just enough" for each material
        readonly Dictionary<string, float> material_thresholds = new Dictionary<string, float> {
            { CO, 1000 },
            { AU, 150 },
            { FE, 15000 },
            { MG, 100 },
            { NI, 1000 },
            { PT, 20 },
            { SI, 1500 },
            { AG, 200 },
            { U, 0 },
            { STONE, 400 },
        };

        readonly Dictionary<string, Dictionary<string, float>> ore_to_ingot_ratios = new Dictionary<string, Dictionary<string, float>> {
            { CO, new Dictionary<string, float>{ { CO, 0.3F } } },
            { AU, new Dictionary<string, float>{ { AU, 0.01F } } },
            { FE, new Dictionary<string, float>{ { FE, 0.7F } } },
            { SCRAP, new Dictionary<string, float>{ { SCRAP, 0.8F } } },
            { MG, new Dictionary<string, float>{ { MG, 0.007F } } },
            { NI, new Dictionary<string, float>{ { NI, 0.4F } } },
            { PT, new Dictionary<string, float>{ { PT, 0.005F } } },
            { SI, new Dictionary<string, float>{ { SI, 0.7F } } },
            { AG, new Dictionary<string, float>{ { AG, 0.1F } } },
            { U, new Dictionary<string, float>{ { U, 0.01F } } },
            // stone is a special case because it will produce multiple ingots
            { STONE,
                new Dictionary<string, float>{
                    { STONE, 0.027F },
                    { FE, 0.054F },
                    { NI, 0.0046F },
                    { SI, 0.007F },
                }
            }
        };

        // statuses for ore and ingots
        readonly Dictionary<string, float> ore_status = new Dictionary<string, float> {
            { CO, 0 },
            { AU, 0 },
            { ICE, 0 },
            { FE, 0 },
            { MG, 0 },
            { NI, 0 },
            { PT, 0 },
            { SI, 0 },
            { AG, 0 },
            { U, 0 },
            { STONE, 0 },
        };
        readonly Dictionary<string, float> ingot_status;
        readonly Dictionary<string, float> storage_ore_status;
        readonly Dictionary<string, float> storage_ingot_status;

        HashSet<string> assembler_ingots;

        // material lists for all ingots used by any production items
        readonly Dictionary<string, List<string>> production_item_ingots = new Dictionary<string, List<string>>
        {
            {"DetectorComponent", new List<string> { FE, NI } },
            {"BulletproofGlass", new List<string> { SI } },
            {"Canvas", new List<string> { FE, SI } },
            {"ComputerComponent", new List<string> { FE, SI } },
            {"ConstructionComponent", new List<string> { FE } },
            {"Display", new List<string> { FE, SI } },
            {"ExplosivesComponent", new List<string> { MG, SI } },
            {"GirderComponent", new List<string> { FE } },
            {"GravityGeneratorComponent", new List<string> { CO, AU, FE, AG } },
            {"InteriorPlate", new List<string> { FE } },
            {"MotorComponent", new List<string> { FE, NI } },
            {"Missile200mm", new List<string> { FE, MG, NI, PT, SI, U } },
            {"MetalGrid", new List<string> { CO, FE, NI } },
            {"MedicalComponent", new List<string> { FE, NI, AG } },
            {"LargeTube", new List<string> { FE } },
            {"NATO_25x184mmMagazine", new List<string> { FE, MG, NI } },
            {"NATO_5p56x45mmMagazine", new List<string> { FE, MG, NI } },
            {"PowerCell", new List<string> { FE, NI, SI } },
            {"RadioCommunicationComponent", new List<string> { FE, SI } },
            {"ReactorComponent", new List<string> { STONE, FE, AG } },
            {"ThrustComponent", new List<string> { CO, AU, FE, PT } },
            {"Superconductor", new List<string> { AU, FE } },
            {"SteelPlate", new List<string> { FE } },
            {"SolarCell", new List<string> { NI, SI } },
            {"SmallTube", new List<string> { FE } },
            {"AngleGrinder", new List<string> { CO, FE, NI, SI } },
            {"AngleGrinder2", new List<string> { CO, FE, NI, SI } },
            {"AngleGrinder3", new List<string> { CO, FE, NI, SI, AG } },
            {"AngleGrinder4", new List<string> { CO, FE, NI, SI, PT } },
            {"AutomaticRifle", new List<string> { FE, NI } },
            {"HydrogenBottle", new List<string> { FE, NI, AG } },
            {"HandDrill4", new List<string> { FE, NI, PT, SI } },
            {"HandDrill3", new List<string> { FE, NI, AG, SI } },
            {"HandDrill2", new List<string> { FE, NI, SI } },
            {"HandDrill", new List<string> { FE, NI, SI } },
            {"OxygenBottle", new List<string> { FE, NI, AG } },
            {"PreciseAutomaticRifle", new List<string> { FE, NI, CO } },
            {"RapidFireAutomaticRifle", new List<string> { FE, NI } },
            {"UltimateAutomaticRifle", new List<string> { FE, NI, PT, AG } },
            {"Welder", new List<string> { CO, FE, NI } },
            {"Welder4", new List<string> { CO, FE, NI, PT } },
            {"Welder3", new List<string> { CO, FE, NI, AG } },
            {"Welder2", new List<string> { CO, FE, NI, SI } },
        };

        /* local data storage, updated once every few cycles */
        List<IMyTerminalBlock> local_blocks = null;
        List<IMyTerminalBlock> local_reactors = null;
        List<IMyTerminalBlock> local_batteries = null;
        List<IMyTerminalBlock> local_refineries = null;
        List<IMyTerminalBlock> local_refineries_subset = null;
        List<IMyTerminalBlock> local_assemblers = null;
        List<IMyTerminalBlock> local_connectors = null;
        List<IMyTerminalBlock> local_storage = null;
        List<IMyTerminalBlock> local_lights = null;
        List<IMyTerminalBlock> local_drills = null;
        List<IMyTerminalBlock> local_grinders = null;
        List<IMyTerminalBlock> local_welders = null;
        List<IMyTerminalBlock> local_text_panels = null;
        List<IMyTerminalBlock> local_air_vents = null;
        List<IMyTerminalBlock> local_oxygen_tanks = null;
        List<IMyTerminalBlock> local_hydrogen_tanks = null;
        List<IMyTerminalBlock> local_oxygen_generators = null;
        List<IMyTerminalBlock> local_power_producers = null;
        List<IMyTerminalBlock> local_trash_connectors = null;
        List<IMyTerminalBlock> local_trash_sensors = null;
        List<IMyTerminalBlock> local_antennas = null;
        List<IMyTerminalBlock> remote_storage = null;
        List<IMyTerminalBlock> remote_ship_storage = null;
        List<IMyCubeGrid> local_grids = null;
        List<IMyCubeGrid> remote_base_grids = null;
        List<IMyCubeGrid> remote_ship_grids = null;
        Dictionary<IMyCubeGrid, GridData> remote_grid_data = null;
        GridData local_grid_data = null;

        // alert levels, in priority order
        enum AlertLevel
        {
            ALERT_RED = 0,
            ALERT_YELLOW,
            ALERT_BLUE,
            ALERT_CYAN,
            ALERT_MAGENTA,
            ALERT_WHITE,
            ALERT_PINK,
            ALERT_BROWN,
            ALERT_GREEN
        };

        class Alert
        {
            public Alert(Color c, string t)
            {
                color = c;
                enabled = false;
                text = t;
            }
            public Color color;
            public bool enabled;
            public string text;
        }

        readonly List<Alert> text_alerts = new List<Alert> {
            new Alert(Color.Red, "Very low storage"),
            new Alert(Color.Yellow, "Low storage"),
            new Alert(Color.Blue, "Low power"),
            new Alert(Color.Cyan, "Ice shortage"),
            new Alert(Color.DarkMagenta, "Clogged"),
            new Alert(Color.White, "Material shortage"),
            new Alert(Color.HotPink, "Damaged blocks"),
            new Alert(Color.Chocolate, "Oxygen leak"),
            new Alert(Color.Green, "Connected"),
        };

        Dictionary<IMyTerminalBlock, int> blocks_to_alerts = new Dictionary<IMyTerminalBlock, int>();
        Dictionary<IMyTerminalBlock, IMyTerminalBlock> disconnected_blocks = new Dictionary<IMyTerminalBlock, IMyTerminalBlock>();

        // alert flags
        const int ALERT_DAMAGED = 0x01;
        const int ALERT_CLOGGED = 0x02;
        const int ALERT_MATERIALS_MISSING = 0x04;
        const int ALERT_LOW_POWER = 0x08;
        const int ALERT_LOW_STORAGE = 0x10;
        const int ALERT_VERY_LOW_STORAGE = 0x20;
        const int ALERT_MATERIAL_SHORTAGE = 0x40;
        const int ALERT_OXYGEN_LEAK = 0x80;
        const int ALERT_LOW_OXYGEN = 0x100;
        const int ALERT_LOW_HYDROGEN = 0x200;
        const int ALERT_CRISIS_THROW_OUT = 0x400;
        const int ALERT_CRISIS_LOCKUP = 0x800;
        const int ALERT_CRISIS_STANDBY = 0x1000;
        const int ALERT_CRISIS_CRITICAL_POWER = 0x2000;
        const int ALERT_DISCONNECTED = 0x4000;

        readonly Dictionary<int, string> block_alerts = new Dictionary<int, string> {
            { ALERT_DAMAGED, "Damaged" },
            { ALERT_CLOGGED, "Clogged" },
            { ALERT_MATERIALS_MISSING, "Materials missing" },
            { ALERT_LOW_POWER, "Low power" },
            { ALERT_LOW_STORAGE, "Low storage" },
            { ALERT_VERY_LOW_STORAGE, "Very low storage" },
            { ALERT_MATERIAL_SHORTAGE, "Material shortage" },
            { ALERT_OXYGEN_LEAK, "Oxygen leak" },
            { ALERT_LOW_OXYGEN, "Low oxygen" },
            { ALERT_LOW_HYDROGEN, "Low hydrogen" },
            { ALERT_CRISIS_THROW_OUT, "Crisis: throwing out ore" },
            { ALERT_CRISIS_LOCKUP, "Crisis: locked up" },
            { ALERT_CRISIS_STANDBY, "Crisis: standing by" },
            { ALERT_CRISIS_CRITICAL_POWER, "Crisis: power level critical" },
            { ALERT_DISCONNECTED, "Block not connected" },
        };

        /* misc local data */
        bool power_above_threshold = false;
        float cur_power_draw;
        float max_power_draw;
        float max_power_output;
        float cur_stored_power;
        float cur_transient_power;
        float cur_stored_oxygen;
        float cur_stored_hydrogen;
        float cur_oxygen_capacity;
        float cur_hydrogen_capacity;
        bool tried_throwing = false;
        bool auto_refuel_ship;
        bool prioritize_uranium = false;
        bool refine_ice = true;
        bool can_use_ingots;
        bool can_use_oxygen;
        bool can_refine;
        bool can_refine_ice;
        bool can_refuel_hydrogen;
        bool can_refuel_oxygen;
        bool large_grid;
        bool has_air_vents;
        bool has_status_panels;
        bool has_reactors;
        bool has_welders;
        bool has_drills;
        bool has_grinders;
        bool has_connectors;
        bool has_oxygen_tanks;
        bool has_hydrogen_tanks;
        bool has_refineries;
        bool connected;
        bool connected_to_base;
        bool connected_to_ship;

        // thrust block definitions
        Dictionary<string, float> thrust_power = new Dictionary<string, float>() {
            { "SmallBlockSmallThrust", 33.6F },
            { "SmallBlockLargeThrust", 400 },
            { "LargeBlockSmallThrust", 560 },
            { "LargeBlockLargeThrust", 6720 },
            { "SmallBlockSmallHydrogenThrust", 0 },
            { "SmallBlockLargeHydrogenThrust", 0 },
            { "LargeBlockSmallHydrogenThrust", 0 },
            { "LargeBlockLargeHydrogenThrust", 0 },
            { "SmallBlockSmallAtmosphericThrust", 700 },
            { "SmallBlockLargeAtmosphericThrust", 2400 },
            { "LargeBlockSmallAtmosphericThrust", 2360 },
            { "LargeBlockLargeAtmosphericThrust", 16360 }
        };

        // power constants - in kWatts
        const float URANIUM_INGOT_POWER = 68760;

        // gas goes 10L per kg of ice
        const float ICE_TO_GAS_RATIO = 10;
        const float LARGE_H2_TANK_CAPACITY = 5000000;
        const float LARGE_O2_TANK_CAPACITY = 100000;
        const float SMALL_H2_TANK_CAPACITY = 160000;
        const float SMALL_O2_TANK_CAPACITY = 50000;

        class ItemHelper
        {
            public IMyTerminalBlock Owner;
            public int InvIdx;
            public MyInventoryItem Item;
            public int Index;
        }
        #endregion

        #region GRAPH

        // data we store about a grid
        // technically, while we use this struct to store data about grids, what we
        // really want is to have an instance of this struct per grid collection, i.e.
        // all grids that are local to each other (connected by rotors or pistons).
        // this is why it's a class, not a struct - so that several grids can share the
        // same instance. it's a crude hack, but it works.
        class GridData
        {
            public bool thrusters;
            public bool wheels;
            public bool welders;
            public bool grinders;
            public bool drills;
            public bool ship;
            public bool @base;
        }

        // grid graph edge class, represents a connection point between two grids.
        class Edge<T>
        {
            public T src { get; set; }
            public T dst { get; set; }
        }

        // comparer for graph edges - the way the comparison is done means the edges are
        // bidirectional - meaning, it doesn't matter which grid is source and which
        // grid is destination, they will be equal as far as comparison is concerned.
        class EdgeComparer<T> : IEqualityComparer<Edge<T>>
        {
            public int GetHashCode(Edge<T> e)
            {
                // multiply src hashcode by dst hashcode - multiplication is commutative, so
                // result will be the same no matter which grid was source or destination
                return e.src.GetHashCode() * e.dst.GetHashCode();
            }
            public bool Equals(Edge<T> e1, Edge<T> e2)
            {
                if (e1.src.Equals(e2.src) && e1.dst.Equals(e2.dst))
                {
                    return true;
                }
                if (e1.src.Equals(e2.dst) && e1.dst.Equals(e2.src))
                {
                    return true;
                }
                return false;
            }
        }

        // our grid graph
        class Graph<T>
        {
            public Graph()
            {
                cmp = new EdgeComparer<T>();
                v_edges = new Dictionary<T, HashSet<Edge<T>>>();
                r_edges = new HashSet<Edge<T>>(cmp);
            }

            // add an edge to the graph
            public void addEdge(T src, T dst, bool is_remote)
            {
                var t = new Edge<T>();
                t.src = src;
                t.dst = dst;

                // remote edges don't need to be added to local list of edges
                if (is_remote)
                {
                    r_edges.Add(t);
                    return;
                }

                // add edge to list of per-vertex edges
                HashSet<Edge<T>> hs_src, hs_dst;
                if (!v_edges.TryGetValue(src, out hs_src))
                {
                    hs_src = new HashSet<Edge<T>>(cmp);
                    v_edges.Add(src, hs_src);
                }
                if (!v_edges.TryGetValue(dst, out hs_dst))
                {
                    hs_dst = new HashSet<Edge<T>>(cmp);
                    v_edges.Add(dst, hs_dst);
                }
                hs_src.Add(t);
                hs_dst.Add(t);
            }

            // get all grids that are local to source grid (i.e. all grids connected by
            // rotors or pistons)
            public List<T> getGridRegion(T src)
            {
                // if there never was a local edge from/to this grid, it's by definition
                // the only grid in this region
                if (!v_edges.ContainsKey(src))
                {
                    return new List<T>() { src };
                }
                // otherwise, gather all vertices in this region
                var region = new List<T>();
                var seen = new HashSet<T>();
                var next = new Queue<T>();
                next.Enqueue(src);
                while (next.Count != 0)
                {
                    var g = next.Dequeue();
                    if (!seen.Contains(g))
                    {
                        var edges = v_edges[g];
                        foreach (var edge in edges)
                        {
                            next.Enqueue(edge.src);
                            next.Enqueue(edge.dst);
                        }
                        seen.Add(g);
                        region.Add(g);
                    }
                }
                return region;
            }

            // this must be called after adding all edges. what this does is, it removes
            // edges that aren't supposed to be there. For example, if you have grids
            // A, B, C, local edges A->B and B->C, and a remote edge C->A, there is a path
            // from C to A through local edges, so the remote edge should not count as an
            // actual "remote" edge, and therefore should be removed.
            public void validateGraph()
            {
                var to_remove = new HashSet<Edge<T>>(cmp);
                var seen = new HashSet<T>();
                foreach (var edge in r_edges)
                {
                    var next = new Queue<T>();
                    next.Enqueue(edge.src);
                    next.Enqueue(edge.dst);
                    while (next.Count != 0)
                    {
                        var g = next.Dequeue();
                        if (!seen.Contains(g))
                        {
                            var region = new HashSet<T>(getGridRegion(g));
                            seen.UnionWith(region);
                            // find any edges that are completely inside this region, and remove them
                            foreach (var e in r_edges)
                            {
                                if (region.Contains(e.src) && region.Contains(e.dst))
                                {
                                    to_remove.Add(e);
                                }
                            }
                        }
                    }
                }
                foreach (var e in to_remove)
                {
                    r_edges.Remove(e);
                }
            }

            // get all neighboring (connected by connectors) grid entry points
            public List<Edge<T>> getGridConnections()
            {
                return new List<Edge<T>>(r_edges);
            }

            // our comparer to use with all sets
            EdgeComparer<T> cmp;
            // list of all edges
            HashSet<Edge<T>> r_edges;
            // dictionaries of edges for each vertex
            Dictionary<T, HashSet<Edge<T>>> v_edges;
        }
        #endregion

        #region UTIL
        // just have a method to indicate that this exception comes from BARABAS
        class BarabasException : Exception
        {
            public BarabasException(string msg, Program pr) : base("BARABAS: " + msg)
            {
                // calling the getX() functions will set off a chain of events if local data
                // is not initialized, so use locally stored data instead
                var panels = pr.local_text_panels;
                if (panels != null && panels.Count > 0)
                {
                    foreach (IMyTextPanel p in panels)
                    {
                        p.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                        p.WriteText(" BARABAS EXCEPTION:\n" + msg);
                    }
                }
                pr.Me.CustomName = "BARABAS Exception: " + msg;
                pr.Me.ShowOnHUD = true;
                if (pr.local_lights != null)
                {
                    pr.showAlertColor(Color.Red);
                }
                var surface = pr.Me.GetSurface(0);
                surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                surface.WriteText("BARABAS Exception: " + msg);
            }
        }

        string getBlockDefinitionStr(IMyTerminalBlock b)
        {
            return b.BlockDefinition.TypeIdString + "/" + b.BlockDefinition.SubtypeName;
        }

        double getMagnitude(char c)
        {
            var pwrs = " kMGTPEZY";
            return Math.Pow(1000, pwrs.IndexOf(c));
        }

        string getMagnitudeStr(float value)
        {
            var pwrs = " kMGTPEZY";
            int pwr_idx = 0;
            while (value >= 1000)
            {
                value /= 1000;
                pwr_idx++;
            }
            if (value >= 100)
                return String.Format("{0:0}", value) + pwrs[pwr_idx];
            else if (value >= 10)
                return String.Format("{0:0.0}", value) + pwrs[pwr_idx];
            else
                return String.Format("{0:0.00}", value) + pwrs[pwr_idx];
        }
        
        int secondsToTicks(float val)
        {
            return (int) Math.Round(60.0 * val);
        }

        /* rounds to 0.1 */
        float ticksToSeconds(int val)
        {
            return (float)Math.Round(val / 60.0, 1);
        }
        #endregion

        #region FILTERS
        /**
         * Filters
         */
        bool excludeBlock(IMyTerminalBlock b)
        {
            if (slimBlock(b) == null)
            {
                return true;
            }
            if (b.CustomName.StartsWith("X"))
            {
                return true;
            }
            if (!b.IsFunctional)
            {
                return true;
            }
            return false;
        }

        bool localGridFilter(IMyTerminalBlock b)
        {
            if (excludeBlock(b))
            {
                return false;
            }
            return getLocalGrids().Contains(b.CubeGrid);
        }

        bool remoteGridFilter(IMyTerminalBlock b)
        {
            if (excludeBlock(b))
            {
                return false;
            }
            return getRemoteGrids().Contains(b.CubeGrid);
        }

        // this filter only gets remote ships - used for tug mode
        bool shipFilter(IMyTerminalBlock b)
        {
            if (excludeBlock(b))
            {
                return false;
            }
            return getShipGrids().Contains(b.CubeGrid);
        }
        #endregion

        #region BLOCKS
        /**
         * Grid and block functions
         */
        // filter blocks by type and locality
        void filterLocalGrid<T>(List<IMyTerminalBlock> blocks, bool ignore_exclude = false)
        {
            var grids = getLocalGrids();
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i];
                bool exclude = false;
                if (!ignore_exclude)
                {
                    exclude = excludeBlock(b);
                }
                if (exclude || !(b is T) || !grids.Contains(b.CubeGrid))
                {
                    blocks.RemoveAt(i);
                }
            }
        }

        // remove null (destroyed) blocks from list
        HashSet<List<IMyTerminalBlock>> null_list;
        List<IMyTerminalBlock> removeNulls(List<IMyTerminalBlock> list)
        {
            if (null_list.Contains(list))
            {
                return list;
            }
            null_list.Add(list);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i];
                if (slimBlock(b) == null || !blockExists(b))
                {
                    blocks_to_alerts.Remove(b);
                    if ((b as IMyRadioAntenna) == null)
                        b.ShowOnHUD = false;
                    list.RemoveAt(i);
                }
            }
            return list;
        }

        IMySlimBlock slimBlock(IMyTerminalBlock b)
        {
            return b.CubeGrid.GetCubeBlock(b.Position);
        }

        bool blockExists(IMyTerminalBlock b)
        {
            return b.CubeGrid.CubeExists(b.Position);
        }

        // does what it says on the tin: picks random subset of a list
        List<IMyTerminalBlock> randomSubset(List<IMyTerminalBlock> list, int limit)
        {
            int len = Math.Min(list.Count, limit);
            var r_idx = new List<int>();
            var result = new List<IMyTerminalBlock>();

            // initialize the index list
            for (int i = 0; i < list.Count; i++)
            {
                r_idx.Add(i);
            }

            // randomize the list
            var rng = new Random();
            for (int i = 0; i < r_idx.Count; i++)
            {
                r_idx.Swap(i, rng.Next(0, r_idx.Count));
            }

            // now, pick out the random subset
            for (int i = 0; i < len; i++)
            {
                result.Add(list[r_idx[i]]);
            }

            return result;
        }

        // get all local blocks
        List<IMyTerminalBlock> getBlocks(bool force_update = false)
        {
            if (local_blocks != null && !force_update)
            {
                return removeNulls(local_blocks);
            }
            filterLocalGrid<IMyTerminalBlock>(local_blocks, true);

            bool alert = false;

            if (hasDisconnectedBlocks())
            {
                alert = true;
            }
            // check if we have unfinished blocks
            for (int i = local_blocks.Count - 1; i >= 0; i--)
            {
                var b = local_blocks[i];
                if (!slimBlock(b).IsFullIntegrity)
                {
                    alert = true;
                    addBlockAlert(b, ALERT_DAMAGED);
                }
                else
                {
                    removeBlockAlert(b, ALERT_DAMAGED);
                }
                updateBlockName(b);
            }
            if (alert)
            {
                addAlert(AlertLevel.ALERT_PINK);
            }
            else
            {
                removeAlert(AlertLevel.ALERT_PINK);
            }
            return local_blocks;
        }

        List<IMyTerminalBlock> getPowerProducers(bool force_update = false)
        {
            if (local_power_producers != null && !force_update)
            {
                return removeNulls(local_power_producers);
            }
            filterLocalGrid<IMyPowerProducer>(local_power_producers);
            return local_power_producers;
        }

        List<IMyTerminalBlock> getReactors(bool force_update = false)
        {
            if (local_reactors != null && !force_update)
            {
                return removeNulls(local_reactors);
            }
            filterLocalGrid<IMyReactor>(local_reactors);
            foreach (var r in local_reactors)
            {
                var inv = r.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                if (items.Count > 1)
                {
                    consolidate(inv);
                }
            }
            return local_reactors;
        }

        List<IMyTerminalBlock> getBatteries(bool force_update = false)
        {
            if (local_batteries != null && !force_update)
            {
                return removeNulls(local_batteries);
            }
            filterLocalGrid<IMyBatteryBlock>(local_batteries);
            for (int i = local_batteries.Count - 1; i >= 0; i--)
            {
                if ((local_batteries[i] as IMyBatteryBlock).ChargeMode == ChargeMode.Recharge)
                {
                    local_batteries.RemoveAt(i);
                }
            }
            return local_batteries;
        }

        List<IMyTerminalBlock> getStorage(bool force_update = false)
        {
            if (local_storage != null && !force_update)
            {
                return removeNulls(local_storage);
            }
            filterLocalGrid<IMyCargoContainer>(local_storage);
            foreach (var s in local_storage)
            {
                var inv = s.GetInventory(0);
                consolidate(inv);
            }
            return local_storage;
        }

        List<IMyTerminalBlock> getRefineries(bool force_update = false)
        {
            if (local_refineries != null && !force_update)
            {
                // if we didn't refresh the list yet, get a random subset
                if (!null_list.Contains(local_refineries_subset))
                {
                    local_refineries_subset = randomSubset(local_refineries, 40);
                }
                return removeNulls(local_refineries_subset);
            }
            refineries_clogged = false;
            filterLocalGrid<IMyRefinery>(local_refineries);
            foreach (IMyRefinery r in local_refineries)
            {
                if (!r.IsQueueEmpty && !r.IsProducing && r.Enabled)
                {
                    addBlockAlert(r, ALERT_CLOGGED);
                    refineries_clogged = true;
                }
                else
                {
                    removeBlockAlert(r, ALERT_CLOGGED);
                }
                updateBlockName(r);
            }
            if (!null_list.Contains(local_refineries_subset))
            {
                local_refineries_subset = randomSubset(local_refineries, 40);
            }
            return local_refineries_subset;
        }

        List<IMyTerminalBlock> getAssemblers(bool force_update = false)
        {
            if (local_assemblers != null && !force_update)
            {
                return removeNulls(local_assemblers);
            }
            assemblers_clogged = false;
            filterLocalGrid<IMyAssembler>(local_assemblers);
            for (int i = local_assemblers.Count - 1; i >= 0; i--)
            {
                var a = local_assemblers[i] as IMyAssembler;
                if (a.Mode == MyAssemblerMode.Disassembly)
                {
                    local_assemblers.RemoveAt(i);
                }
                else
                {
                    consolidate(a.GetInventory(0));
                    consolidate(a.GetInventory(1));
                    var in_inv = a.GetInventory(0);
                    var out_inv = a.GetInventory(1);
                    float inputL = (float)in_inv.CurrentVolume / (float)in_inv.MaxVolume;
                    float outputL = (float)out_inv.CurrentVolume / (float)out_inv.MaxVolume;
                    bool isWaiting = !a.IsQueueEmpty && !a.IsProducing;
                    removeBlockAlert(a, ALERT_MATERIALS_MISSING);
                    removeBlockAlert(a, ALERT_CLOGGED);
                    if ((inputL > 0.98F || outputL > 0.98F) && isWaiting && a.Enabled)
                    {
                        addBlockAlert(a, ALERT_CLOGGED);
                        assemblers_clogged = true;
                    }
                    else if (isWaiting)
                    {
                        addBlockAlert(a, ALERT_MATERIALS_MISSING);
                        assemblers_clogged = true;
                    }
                    updateBlockName(a);
                }
            }
            return local_assemblers;
        }

        List<IMyTerminalBlock> getConnectors(bool force_update = false)
        {
            if (local_connectors != null && !force_update)
            {
                return removeNulls(local_connectors);
            }
            filterLocalGrid<IMyShipConnector>(local_connectors);
            foreach (IMyShipConnector c in local_connectors)
            {
                consolidate(c.GetInventory(0));
                // prepare the connector
                // at this point, we have already turned off the conveyors,
                // so now just check if we aren't throwing anything already and
                // if we aren't in "collect all" mode
                c.CollectAll = false;
            }
            return local_connectors;
        }

        // get notification lights
        List<IMyTerminalBlock> getLights(bool force_update = false)
        {
            if (local_lights != null && !force_update)
            {
                return removeNulls(local_lights);
            }
            // find our group
            local_lights = new List<IMyTerminalBlock>();
            var g = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");
            if (g != null)
            {
                g.GetBlocks(local_lights, localGridFilter);
            }
            filterLocalGrid<IMyLightingBlock>(local_lights);
            return local_lights;
        }

        // get status report text panels
        List<IMyTerminalBlock> getTextPanels(bool force_update = false)
        {
            if (local_text_panels != null && !force_update)
            {
                return removeNulls(local_text_panels);
            }
            // find our group
            local_text_panels = new List<IMyTerminalBlock>();

            var g = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");

            if (g != null)
            {
                g.GetBlocks(local_text_panels, localGridFilter);
            }

            // we may find multiple Status groups, as we may have a BARABAS-driven
            // ships connected, so let's filter text panels
            filterLocalGrid<IMyTextPanel>(local_text_panels);

            return local_text_panels;
        }

        // get status report text panels
        List<IMyTerminalBlock> getAntennas(bool force_update = false)
        {
            if (local_antennas != null && !force_update)
            {
                return removeNulls(local_antennas);
            }
            // find our group
            local_antennas = new List<IMyTerminalBlock>();
            var g = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");

            if (g != null)
            {
                var a = new List<IMyTerminalBlock>();
                var b = new List<IMyTerminalBlock>();
                var l = new List<IMyTerminalBlock>();
                g.GetBlocks(b);
                g.GetBlocks(a);
                g.GetBlocks(l);

                // we may find multiple Status groups, as we may have a BARABAS-driven
                // ships connected, so let's filter text panels
                filterLocalGrid<IMyBeacon>(b);
                filterLocalGrid<IMyRadioAntenna>(a);
                filterLocalGrid<IMyLaserAntenna>(l);

                // populate the list
                local_antennas.AddRange(b);
                local_antennas.AddRange(a);
                local_antennas.AddRange(l);
            }

            return local_antennas;
        }

        List<IMyTerminalBlock> getDrills(bool force_update = false)
        {
            if (local_drills != null && !force_update)
            {
                return removeNulls(local_drills);
            }
            filterLocalGrid<IMyShipDrill>(local_drills);
            foreach (var d in local_drills)
            {
                consolidate(d.GetInventory(0));
            }
            return local_drills;
        }

        List<IMyTerminalBlock> getGrinders(bool force_update = false)
        {
            if (local_grinders != null && !force_update)
            {
                return removeNulls(local_grinders);
            }
            filterLocalGrid<IMyShipGrinder>(local_grinders);
            foreach (var g in local_grinders)
            {
                consolidate(g.GetInventory(0));
            }
            return local_grinders;
        }

        List<IMyTerminalBlock> getWelders(bool force_update = false)
        {
            if (local_welders != null && !force_update)
            {
                return removeNulls(local_welders);
            }
            filterLocalGrid<IMyShipWelder>(local_welders);
            foreach (var w in local_welders)
            {
                consolidate(w.GetInventory(0));
            }
            return local_welders;
        }

        List<IMyTerminalBlock> getAirVents(bool force_update = false)
        {
            if (local_air_vents != null && !force_update)
            {
                return removeNulls(local_air_vents);
            }
            filterLocalGrid<IMyAirVent>(local_air_vents);
            return local_air_vents;
        }

        List<IMyTerminalBlock> getOxygenTanks(bool force_update = false)
        {
            if (local_oxygen_tanks != null && !force_update)
            {
                return removeNulls(local_oxygen_tanks);
            }
            filterLocalGrid<IMyGasTank>(local_oxygen_tanks);
            return local_oxygen_tanks;
        }

        List<IMyTerminalBlock> getHydrogenTanks(bool force_update = false)
        {
            if (local_hydrogen_tanks != null && !force_update)
            {
                return removeNulls(local_hydrogen_tanks);
            }
            filterLocalGrid<IMyGasTank>(local_hydrogen_tanks);
            return local_hydrogen_tanks;
        }

        List<IMyTerminalBlock> getOxygenGenerators(bool force_update = false)
        {
            if (local_oxygen_generators != null && !force_update)
            {
                return removeNulls(local_oxygen_generators);
            }
            filterLocalGrid<IMyGasGenerator>(local_oxygen_generators);
            return local_oxygen_generators;
        }

        IMyCubeGrid getConnectedGrid(IMyShipConnector c)
        {
            if (c.Status != MyShipConnectorStatus.Connected)
            {
                return null;
            }
            // skip connectors connecting to the same grid
            var o = c.OtherConnector;
            if (o.CubeGrid == c.CubeGrid)
            {
                return null;
            }
            return o.CubeGrid;
        }

        IMyCubeGrid getConnectedGrid(IMyMotorBase r)
        {
            if (!r.IsAttached)
            {
                return null;
            }
            return r.TopGrid;
        }

        IMyCubeGrid getConnectedGrid(IMyPistonBase p)
        {
            if (!p.IsAttached)
            {
                return null;
            }
            return p.TopGrid;
        }

        // getting local grids is not trivial, we're basically building a graph of all
        // grids and figure out which ones are local to us. we are also populating
        // object lists in the meantime
        List<IMyCubeGrid> getLocalGrids(bool force_update = false)
        {
            if (local_grids != null && !force_update)
            {
                return local_grids;
            }

            // clear all lists
            local_blocks = new List<IMyTerminalBlock>();
            local_reactors = new List<IMyTerminalBlock>();
            local_batteries = new List<IMyTerminalBlock>();
            local_refineries = new List<IMyTerminalBlock>();
            local_assemblers = new List<IMyTerminalBlock>();
            local_connectors = new List<IMyTerminalBlock>();
            local_storage = new List<IMyTerminalBlock>();
            local_drills = new List<IMyTerminalBlock>();
            local_grinders = new List<IMyTerminalBlock>();
            local_welders = new List<IMyTerminalBlock>();
            local_air_vents = new List<IMyTerminalBlock>();
            local_oxygen_tanks = new List<IMyTerminalBlock>();
            local_hydrogen_tanks = new List<IMyTerminalBlock>();
            local_oxygen_generators = new List<IMyTerminalBlock>();
            local_power_producers = new List<IMyTerminalBlock>();
            // piston and rotor lists are local, we don't need them once we're done
            var pistons = new List<IMyTerminalBlock>();
            var rotors = new List<IMyTerminalBlock>();

            // grid data for all the grids we discover
            var tmp_grid_data = new Dictionary<IMyCubeGrid, GridData>();

            // get all blocks that are accessible to GTS
            GridTerminalSystem.GetBlocks(local_blocks);

            // for each block, get its grid, store data for this grid, and populate respective
            // object list if it's one of the objects we're interested in
            foreach (var b in local_blocks)
            {
                if (slimBlock(b) == null)
                {
                    continue;
                }
                GridData data;
                if (!tmp_grid_data.TryGetValue(b.CubeGrid, out data))
                {
                    data = new GridData();
                    tmp_grid_data.Add(b.CubeGrid, data);
                }
                // add all power producers to the list independently of what they are
                if (b is IMyPowerProducer)
                {
                    local_power_producers.Add(b);
                }

                // fill all lists
                if (b is IMyReactor)
                {
                    local_reactors.Add(b);
                }
                else if (b is IMyBatteryBlock)
                {
                    local_batteries.Add(b);
                }
                else if (b is IMyRefinery)
                {
                    local_refineries.Add(b);
                }
                // exclude survival kits
                else if (b is IMyAssembler && !getBlockDefinitionStr(b).Contains("SurvivalKit"))
                {
                    local_assemblers.Add(b);
                }
                else if (b is IMyShipConnector)
                {
                    local_connectors.Add(b);

                    // also add connected grid
                    var c = b as IMyShipConnector;
                    var g = getConnectedGrid(c);
                    if (g != null && !tmp_grid_data.TryGetValue(g, out data))
                    {
                        data = new GridData();
                        tmp_grid_data.Add(g, data);
                    }
                }
                else if (b is IMyCargoContainer)
                {
                    local_storage.Add(b);
                }
                else if (b is IMyShipDrill)
                {
                    local_drills.Add(b);
                    data.drills = true;
                }
                else if (b is IMyShipGrinder)
                {
                    local_grinders.Add(b);
                    data.grinders = true;
                }
                else if (b is IMyShipWelder)
                {
                    local_welders.Add(b);
                    data.welders = true;
                }
                else if (b is IMyAirVent)
                {
                    local_air_vents.Add(b);
                }
                else if (b is IMyGasTank)
                {
                    // oxygen and hydrogen tanks are of the same type, but differ in definitions
                    if (b.BlockDefinition.SubtypeName.Contains("Hydrogen"))
                    {
                        local_hydrogen_tanks.Add(b);
                    }
                    else
                    {
                        local_oxygen_tanks.Add(b);
                    }
                }
                else if (b is IMyGasGenerator)
                {
                    local_oxygen_generators.Add(b);
                }
                else if (b is IMyPistonBase)
                {
                    pistons.Add(b);

                    // also add connected grid
                    var p = b as IMyPistonBase;
                    var g = getConnectedGrid(p);
                    if (g != null && !tmp_grid_data.TryGetValue(g, out data))
                    {
                        data = new GridData();
                        tmp_grid_data.Add(g, data);
                    }
                }
                else if (b is IMyMotorSuspension)
                {
                    data.wheels = true;
                }
                else if (b is IMyMotorBase)
                {
                    rotors.Add(b);

                    // also add connected grid
                    var r = b as IMyMotorBase;
                    var g = getConnectedGrid(r);
                    if (g != null && !tmp_grid_data.TryGetValue(g, out data))
                    {
                        data = new GridData();
                        tmp_grid_data.Add(g, data);
                    }
                }
                else if (b is IMyThrust)
                {
                    data.thrusters = true;
                }
                else if (b is IMyProgrammableBlock && b != Me && b.CubeGrid != Me.CubeGrid)
                {
                    // skip disabled CPU's as well
                    if (b.CustomName == "BARABAS Ship CPU")
                    {
                        data.ship = true;
                    }
                    else if (b.CustomName == "BARABAS Base CPU")
                    {
                        data.@base = true;
                    }
                }
            }

            // now, build a graph of all grids
            var gr = new Graph<IMyCubeGrid>();
            var grids = new List<IMyCubeGrid>(tmp_grid_data.Keys);

            // first, go through all pistons
            foreach (IMyPistonBase p in pistons)
            {
                var connected_grid = getConnectedGrid(p);

                if (connected_grid != null)
                {
                    // grids connected to pistons are local to their source
                    gr.addEdge(p.CubeGrid, connected_grid, false);
                }
            }

            // do the same for rotors
            foreach (IMyMotorBase rotor in rotors)
            {
                var connected_grid = getConnectedGrid(rotor);

                if (connected_grid != null)
                {
                    // grids connected to locals are local to their source
                    gr.addEdge(rotor.CubeGrid, connected_grid, false);
                }
            }

            // do the same for connectors
            foreach (IMyShipConnector c in local_connectors)
            {
                var connected_grid = getConnectedGrid(c);

                if (connected_grid != null)
                {
                    // grids connected to connectors belong to a different ship
                    gr.addEdge(c.CubeGrid, connected_grid, true);
                }
            }

            // make sure we remove all unnecessary edges from the graph
            gr.validateGraph();

            // now, get our actual local grid
            local_grids = gr.getGridRegion(Me.CubeGrid);

            // store our new local grid data
            local_grid_data = new GridData();
            foreach (var grid in local_grids)
            {
                local_grid_data.wheels |= tmp_grid_data[grid].wheels;
                local_grid_data.thrusters |= tmp_grid_data[grid].thrusters;
                local_grid_data.drills |= tmp_grid_data[grid].drills;
                local_grid_data.grinders |= tmp_grid_data[grid].grinders;
                local_grid_data.welders |= tmp_grid_data[grid].welders;
                local_grid_data.@base |= tmp_grid_data[grid].@base;
                local_grid_data.ship |= tmp_grid_data[grid].ship;
            }

            // now, go through all the connector-to-connector grid connections
            var connections = gr.getGridConnections();
            // we don't want to count known local grids as remote, so mark all local grids
            // as ones we've seen already
            var seen = new HashSet<IMyCubeGrid>(local_grids);

            remote_grid_data = new Dictionary<IMyCubeGrid, GridData>();

            foreach (var e in connections)
            {
                // we may end up with two unknown grids
                var edge_grids = new List<IMyCubeGrid>() { e.src, e.dst };

                foreach (var e_g in edge_grids)
                {
                    // if we found a new grid
                    if (!seen.Contains(e_g))
                    {
                        // get all grids that are local to it
                        GridData data;
                        var r_grids = gr.getGridRegion(e_g);
                        if (!remote_grid_data.TryGetValue(e_g, out data))
                        {
                            data = new GridData();
                        }
                        // store their properties
                        foreach (var g in r_grids)
                        {
                            data.wheels |= tmp_grid_data[g].wheels;
                            data.thrusters |= tmp_grid_data[g].thrusters;
                            data.drills |= tmp_grid_data[g].drills;
                            data.grinders |= tmp_grid_data[g].grinders;
                            data.welders |= tmp_grid_data[g].welders;
                            data.@base |= tmp_grid_data[g].@base;
                            data.ship |= tmp_grid_data[g].ship;
                            remote_grid_data.Add(g, data);
                            seen.Add(g);
                        }
                    }
                }
            }

            return local_grids;
        }

        // we already know what the remote grids are (we've figured that out when we
        // were looking for local grids), so all that's left now is to decide whether
        // these grids are base or ship grids
        void findRemoteGrids()
        {
            if (remote_grid_data.Count == 0)
            {
                remote_base_grids = new List<IMyCubeGrid>();
                remote_ship_grids = new List<IMyCubeGrid>();
                connected_to_base = false;
                connected_to_ship = false;
                connected = false;
                return;
            }
            var b_g = new List<IMyCubeGrid>();
            var s_g = new List<IMyCubeGrid>();
            // we need to know how many bases we've found
            var base_grid_info = new HashSet<GridData>();

            foreach (var pair in remote_grid_data)
            {
                var grid = pair.Key;
                var data = pair.Value;
                if (data.ship)
                {
                    s_g.Add(grid);
                    continue;
                }
                else if (data.@base)
                {
                    base_grid_info.Add(data);
                    b_g.Add(grid);
                    continue;
                }
                // if we're a base, assume every other grid is a ship unless we're explicitly
                // told that it's another base
                if (data.thrusters || data.wheels || isBaseMode())
                {
                    s_g.Add(grid);
                }
                else
                {
                    b_g.Add(grid);
                    base_grid_info.Add(data);
                }
            }
            // we can't have multiple bases as we need to know where to push stuff
            if (isShipMode() && base_grid_info.Count > 1)
            {
                throw new BarabasException("Cannot have more than one base", this);
            }
            remote_base_grids = b_g;
            remote_ship_grids = s_g;
            connected_to_base = b_g.Count > 0;
            connected_to_ship = s_g.Count > 0;
            connected = connected_to_base || connected_to_ship;
        }

        List<IMyCubeGrid> getShipGrids()
        {
            return remote_ship_grids;
        }

        List<IMyCubeGrid> getRemoteGrids()
        {
            if (isBaseMode())
            {
                return remote_ship_grids;
            }
            else
            {
                return remote_base_grids;
            }
        }

        List<IMyTerminalBlock> getRemoteStorage(bool force_update = false)
        {
            if (remote_storage != null && !force_update)
            {
                return removeNulls(remote_storage);
            }
            remote_storage = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(remote_storage, remoteGridFilter);
            foreach (var s in remote_storage)
            {
                consolidate(s.GetInventory(0));
            }
            return remote_storage;
        }

        List<IMyTerminalBlock> getRemoteShipStorage(bool force_update = false)
        {
            if (remote_ship_storage != null && !force_update)
            {
                return removeNulls(remote_ship_storage);
            }
            remote_ship_storage = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(remote_ship_storage, shipFilter);
            foreach (var s in remote_ship_storage)
            {
                consolidate(s.GetInventory(0));
            }
            return remote_ship_storage;
        }

        void getRemoteOxyHydroLevels()
        {
            var bs = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyGasTank>(bs, remoteGridFilter);
            float o2_cur = 0;
            float h2_cur = 0;
            float o2_needed = (large_grid ? LARGE_O2_TANK_CAPACITY : SMALL_O2_TANK_CAPACITY) / 2;
            float h2_needed = (large_grid ? LARGE_H2_TANK_CAPACITY : SMALL_H2_TANK_CAPACITY) / 2;
            for (int i = bs.Count - 1; i >= 0; i--)
            {
                var b = bs[i] as IMyGasTank;
                if (b.Stockpile || slimBlock(b) == null)
                {
                    continue;
                }

                bool sz = b.BlockDefinition.SubtypeName.Contains("Large");

                if (b.BlockDefinition.SubtypeName.Contains("Hydrogen"))
                {
                    h2_cur += (float) b.FilledRatio * (sz ? LARGE_H2_TANK_CAPACITY : SMALL_H2_TANK_CAPACITY);
                }
                else
                {
                    o2_cur += (float) b.FilledRatio * (sz ? LARGE_O2_TANK_CAPACITY : SMALL_O2_TANK_CAPACITY);
                }
            }
            // if we have at least half a tank, we can refuel
            can_refuel_hydrogen = h2_cur > h2_needed;
            can_refuel_oxygen = o2_cur > o2_needed;
        }

        // get local trash disposal connector
        List<IMyTerminalBlock> getTrashConnectors(bool force_update = false)
        {
            if (local_trash_connectors != null && !force_update)
            {
                return removeNulls(local_trash_connectors);
            }

            // find our group
            local_trash_connectors = new List<IMyTerminalBlock>();

            var g = GridTerminalSystem.GetBlockGroupWithName("BARABAS Trash");

            if (g != null)
            {
                g.GetBlocks(local_trash_connectors);
            }

            // we may find multiple Trash groups, as we may have a BARABAS-driven
            // ships connected, so let's filter connectors
            filterLocalGrid<IMyShipConnector>(local_trash_connectors);

            // if we still have no trash connectors, use the first one available
            if (local_trash_connectors.Count == 0 && getConnectors().Count > 0)
            {
                local_trash_connectors.Add(getConnectors()[0]);
            }

            return local_trash_connectors;
        }

        // get local trash disposal sensors
        List<IMyTerminalBlock> getTrashSensors(bool force_update = false)
        {
            if (local_trash_sensors != null && !force_update)
            {
                return removeNulls(local_trash_sensors);
            }

            // find our group
            local_trash_sensors = new List<IMyTerminalBlock>();

            var g = GridTerminalSystem.GetBlockGroupWithName("BARABAS Trash");

            if (g != null)
            {
                g.GetBlocks(local_trash_sensors);
            }

            // we may find multiple Trash groups, as we may have a BARABAS-driven
            // ships connected, so let's filter sensors
            filterLocalGrid<IMySensorBlock>(local_trash_sensors);

            return local_trash_sensors;
        }
        #endregion

        #region INVENTORY
        /**
         * Inventory access functions
         */
        // check if there are still any disconnected blocks
        bool hasDisconnectedBlocks()
        {
            var to_delete = new List<IMyTerminalBlock>();
            foreach (var p in disconnected_blocks)
            {
                var src = p.Key;
                var dst = p.Value;

                if (!blockExists(src) || !blockExists(dst))
                {
                    removeBlockAlert(src, ALERT_DISCONNECTED);
                    removeBlockAlert(dst, ALERT_DISCONNECTED);
                    to_delete.Add(src);
                    continue;
                }
                var src_inv = src.GetInventory(0);
                var dst_inv = dst.GetInventory(0);
                if (src_inv.IsConnectedTo(dst_inv))
                {
                    removeBlockAlert(src, ALERT_DISCONNECTED);
                    removeBlockAlert(dst, ALERT_DISCONNECTED);
                    to_delete.Add(src);
                }
            }
            foreach (var key in to_delete)
            {
                disconnected_blocks.Remove(key);
            }
            return disconnected_blocks.Count != 0;
        }

        // get all ingots of a certain type from a particular inventory
        void getAllIngots(IMyTerminalBlock b, int srcInv, string name, List<ItemHelper> list)
        {
            var inv = b.GetInventory(srcInv);
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!isIngot(item))
                {
                    continue;
                }
                if (name != null && item.Type.SubtypeId != name)
                {
                    continue;
                }
                ItemHelper ih = new ItemHelper();
                ih.InvIdx = srcInv;
                ih.Item = item;
                ih.Index = i;
                ih.Owner = b;
                list.Add(ih);
            }
        }

        // get all ingots of a certain type from a particular inventory
        void getAllOre(IMyTerminalBlock b, int srcInv, string name, List<ItemHelper> list)
        {
            var inv = b.GetInventory(srcInv);
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!isOre(item))
                {
                    continue;
                }
                if (name != null && item.Type.SubtypeId != name)
                {
                    continue;
                }
                ItemHelper ih = new ItemHelper();
                ih.InvIdx = srcInv;
                ih.Item = item;
                ih.Index = i;
                ih.Owner = b;
                list.Add(ih);
            }
        }

        // get all ingots residing in storage
        List<ItemHelper> getAllStorageOre(string name = null)
        {
            var list = new List<ItemHelper>();
            var blocks = getStorage();
            foreach (var b in blocks)
            {
                getAllOre(b, 0, name, list);
            }
            return list;
        }

        List<ItemHelper> getAllStorageIngots(string name = null)
        {
            var list = new List<ItemHelper>();
            var blocks = getStorage();
            foreach (var b in blocks)
            {
                getAllIngots(b, 0, name, list);
            }
            return list;
        }

        bool isOre(MyInventoryItem i)
        {
            if (i.Type.SubtypeId == "Scrap")
            {
                return true;
            }
            return i.Type.TypeId.Equals("MyObjectBuilder_Ore");
        }

        bool isIngot(MyInventoryItem i)
        {
            if (i.Type.SubtypeId == "Scrap")
            {
                return false;
            }
            return i.Type.TypeId.Equals("MyObjectBuilder_Ingot");
        }

        bool isComponent(MyInventoryItem i)
        {
            return i.Type.TypeId.Equals("MyObjectBuilder_Component");
        }

        // get total amount of all ingots (of a particular type) stored in a particular inventory
        float getTotalIngots(IMyTerminalBlock b, int srcInv, string name)
        {
            var entries = new List<ItemHelper>();
            getAllIngots(b, srcInv, name, entries);
            float ingots = 0;
            foreach (var e in entries)
            {
                ingots += (float)e.Item.Amount;
            }
            return ingots;
        }

        /**
         * Inventory manipulation functions
         */
        void consolidate(IMyInventory inv)
        {
            var posmap = new Dictionary<string, int>();
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            bool needs_consolidation = false;
            // go through all items and note the first time they appear in the inventory
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string str = item.Type.TypeId + item.Type.SubtypeId;
                if (!posmap.ContainsKey(str))
                {
                    posmap[str] = i;
                }
                else
                {
                    needs_consolidation = true;
                }
            }
            // make sure we don't touch already consolidated inventories
            if (!needs_consolidation)
            {
                return;
            }
            // now, consolidate all items
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                string str = item.Type.TypeId + item.Type.SubtypeId;
                int dstIndex = posmap[str];
                inv.TransferItemTo(inv, i, dstIndex, true, item.Amount);
            }
        }

        // make sure we process ore in chunks, prevent one ore clogging the refinery
        void rebalance(IMyInventory inv)
        {
            // make note of how much was the first item
            float? first_amount = null;
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            if (items.Count > 1)
            {
                first_amount = (float)Math.Min((float)items[0].Amount, CHUNK_SIZE);
            }
            consolidate(inv);
            items.Clear();
            inv.GetItems(items);
            // skip if we have no work to do
            if (items.Count < 2)
            {
                return;
            }

            for (int i = 0; i < (float)Math.Min(items.Count, ore_types.Count); i++)
            {
                var item = items[i];

                // check if we have enough ore
                if ((float)item.Amount > CHUNK_SIZE)
                {
                    float amount = 0;
                    if (i == 0 && first_amount.HasValue)
                    {
                        amount = (float)item.Amount - first_amount.Value;
                    }
                    else
                    {
                        amount = (float)item.Amount - CHUNK_SIZE;
                    }
                    pushBack(inv, i, (VRage.MyFixedPoint)Math.Round(amount, 4));
                }
            }
        }

        float TryTransfer(IMyTerminalBlock src, int srcInv, IMyTerminalBlock dst, int dstInv,
                            int srcIndex, int? dstIndex, bool? stack, VRage.MyFixedPoint? amount)
        {
            var src_inv = src.GetInventory(srcInv);
            var dst_inv = dst.GetInventory(dstInv);
            var src_items = new List<MyInventoryItem>();
            src_inv.GetItems(src_items);
            var src_count = src_items.Count;
            var src_amount = (float)src_items[srcIndex].Amount;

            if (!src_inv.TransferItemTo(dst_inv, srcIndex, dstIndex, stack, amount))
            {
                var sb = new StringBuilder();
                sb.Append("Error transfering from ");
                sb.Append(getBlockName(src));
                sb.Append(" to ");
                sb.Append(getBlockName(dst));
                Echo(sb.ToString());
                Echo("Check conveyors for missing/damage and\nblock ownership");
                if (!disconnected_blocks.ContainsKey(src))
                {
                    disconnected_blocks.Add(src, dst);
                }
                addBlockAlert(src, ALERT_DISCONNECTED);
                addBlockAlert(dst, ALERT_DISCONNECTED);
                return -1;
            }
            src_items.Clear();
            src_inv.GetItems(src_items);

            // if count changed, we transferred all of it
            if (src_count != src_items.Count)
            {
                return src_amount;
            }

            // if count didn't change, return the difference between src and cur amount
            var cur_amount = (float)src_items[srcIndex].Amount;

            return src_amount - cur_amount;
        }

        bool Transfer(IMyTerminalBlock src, int srcInv, IMyTerminalBlock dst, int dstInv,
                        int srcIndex, int? dstIndex, bool? stack, VRage.MyFixedPoint? amount)
        {
            if (src == dst)
            {
                return true;
            }
            return TryTransfer(src, srcInv, dst, dstInv, srcIndex, dstIndex, stack, amount) > 0;
        }

        void pushBack(IMyInventory src, int srcIndex, VRage.MyFixedPoint? amount)
        {
            var items = new List<MyInventoryItem>();
            src.GetItems(items);
            src.TransferItemTo(src, srcIndex, items.Count, true, amount);
        }

        void pushFront(IMyInventory src, int srcIndex, VRage.MyFixedPoint? amount)
        {
            src.TransferItemTo(src, srcIndex, 0, true, amount);
        }
        #endregion

        #region STORAGE
        /**
         * Volume & storage load functions
         */
        float getTotalStorageLoad()
        {
            var s = getStorage();

            float cur_vol = 0;
            float max_vol = 0;
            float r;
            foreach (var c in s)
            {
                cur_vol += (float)c.GetInventory(0).CurrentVolume;
                max_vol += (float)c.GetInventory(0).MaxVolume;
            }
            r = (float)Math.Round(cur_vol / max_vol, 2);

            if (isSpecializedShipMode())
            {
                r = (float)Math.Round(r * 0.75F, 4);
            }
            else
            {
                return r;
            }
            // if we're a drill ship or a grinder, also look for block with the biggest load
            if (isDrillMode())
            {
                s = getDrills();
            }
            else if (isGrinderMode())
            {
                s = getGrinders();
            }
            else if (isWelderMode())
            {
                s = getWelders();
            }
            else
            {
                throw new BarabasException("Unknown mode", this);
            }
            float maxL = 0;
            foreach (var c in s)
            {
                var inv = c.GetInventory(0);
                var L = (float)inv.CurrentVolume / (float)inv.MaxVolume;
                if (L > maxL)
                {
                    maxL = L;
                }
            }
            // scale the drill/grinder load to fit in the last 25% of the storage
            // the result of this is, when the storage is full, yellow alert goes off,
            // when drills/grinders are full, red alert goes off
            r = r + maxL * 0.25F;
            return r;
        }

        // we are interested only in stuff stored in storage and in tools
        float getTotalStorageMass()
        {
            var s = new List<IMyTerminalBlock>();
            s.AddRange(getStorage());
            s.AddRange(getDrills());
            s.AddRange(getWelders());
            s.AddRange(getGrinders());

            float cur_mass = 0;
            foreach (var c in s)
            {
                cur_mass += (float)c.GetInventory(0).CurrentMass;
            }
            return cur_mass;
        }

        // decide if a refinery can accept certain ore - this is done to prevent
        // clogging all refineries with single ore
        bool canAcceptOre(IMyInventory inv, string name)
        {
            float left = ((float)inv.MaxVolume - (float)inv.CurrentVolume) * 1000;
            if (left > 1500)
            {
                return true;
            }
            if (left < 100 || name == ICE)
            {
                return false;
            }

            // if this is a priority ore, accept it unconditionally
            if (can_use_ingots && storage_ingot_status[name] < material_thresholds[name] * material_threshold_multiplier)
            {
                return true;
            }
            // if no ore is priority, don't clog the refinery
            if (left < 600)
            {
                return false;
            }

            // aim for equal spread
            var ores = new Dictionary<string, float>();
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            bool seenCur = false;
            foreach (var i in items)
            {
                var ore = i.Type.SubtypeId;
                float amount;
                ores.TryGetValue(ore, out amount);
                ores[ore] = amount + (float)i.Amount;
                if (ore == name)
                {
                    seenCur = true;
                }
            }
            int keyCount = ores.Keys.Count;
            if (!seenCur)
            {
                keyCount++;
            }
            // don't clog refinery with single ore
            if (keyCount < 2)
            {
                if (left < 1000)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            float cur;
            ores.TryGetValue(name, out cur);
            float target = ((float)inv.CurrentVolume * 1000) / keyCount;
            return cur < target;
        }

        bool hasOnlyOre(IMyInventory inv)
        {
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            foreach (var i in items)
            {
                if (!isOre(i))
                {
                    return false;
                }
            }
            return true;
        }

        bool hasOnlyIngots(IMyInventory inv)
        {
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            foreach (var i in items)
            {
                if (!isIngot(i))
                {
                    return false;
                }
            }
            return true;
        }

        bool hasOnlyComponents(IMyInventory inv)
        {
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            foreach (var i in items)
            {
                // we don't care about components specifically; rather, we care if it
                // has ore or ingots
                if (isOre(i) || isIngot(i))
                {
                    return false;
                }
            }
            return true;
        }

        void checkStorageLoad()
        {
            if (getStorage().Count == 0)
            {
                status_report[STATUS_STORAGE_LOAD] = "No storage found";
                removeAlert(AlertLevel.ALERT_YELLOW);
                removeAlert(AlertLevel.ALERT_RED);
                return;
            }
            float load = getTotalStorageLoad();
            if (load >= 0.98F)
            {
                addAlert(AlertLevel.ALERT_RED);
                removeAlert(AlertLevel.ALERT_YELLOW);
                // if we're a base, enter crisis mode
                bool have_ore = false;
                foreach (var ore in ore_types)
                {
                    if (ore == ICE)
                    {
                        continue;
                    }
                    if (storage_ore_status[ore] > 0)
                    {
                        have_ore = true;
                        break;
                    }
                }
                bool try_crisis = have_ore && refineriesClogged();
                try_crisis |= !have_ore;
                if (isBaseMode() && has_refineries && try_crisis)
                {
                    if (tried_throwing)
                    {
                        storeTrash(true);
                        crisis_mode = CrisisMode.CRISIS_MODE_LOCKUP;
                    }
                    else
                    {
                        crisis_mode = CrisisMode.CRISIS_MODE_THROW_ORE;
                    }
                }
            }
            else
            {
                removeAlert(AlertLevel.ALERT_RED);
            }

            if (crisis_mode == CrisisMode.CRISIS_MODE_THROW_ORE && load < 0.98F)
            {
                // exit crisis mode, but "tried_throwing" still reminds us that we
                // have just thrown out ore - if we end up in a crisis again, we'll
                // go lockup instead of throwing ore
                crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                tried_throwing = true;
                storeTrash(true);
            }
            if (load >= 0.75F && load < 0.98F)
            {
                storeTrash();
                addAlert(AlertLevel.ALERT_YELLOW);
            }
            else if (load < 0.75F)
            {
                removeAlert(AlertLevel.ALERT_YELLOW);
                storeTrash();
                if (load < 0.98F && isBaseMode())
                {
                    tried_throwing = false;
                }
            }
            float mass = getTotalStorageMass();

            status_report[STATUS_STORAGE_LOAD] = String.Format("{0}% / {1}", (float)Math.Round(load * 100, 0), getMagnitudeStr(mass));
        }

        void turnOffConveyors()
        {
            var blocks = getBlocks();
            // go through all blocks and set "use conveyor" to off
            foreach (var b in blocks)
            {
                if (b is IMyAssembler)
                {
                    continue;
                }
                if (b is IMyShipWelder)
                {
                    continue;
                }

                var prod = b as IMyProductionBlock;
                if (prod != null)
                {
                    prod.UseConveyorSystem = false;
                }
            }
        }

        /**
         * Push and pull from storage
         */
        // go through one item at a time - over time, local storage will sort itself
        void sortLocalStorage()
        {
            var s = getStorage();
            // don't sort if there are less than three containers
            if (s.Count < 3)
            {
                return;
            }
            int n_sort = 0;

            // for each container, transfer one item
            for (int c = 0; c < s.Count; c++)
            {
                var container = s[c];
                var inv = container.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                var vol = (float)inv.CurrentVolume;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    bool itemIsOre = isOre(item);
                    bool itemIsIngot = isIngot(item);
                    bool itemIsComponent = !itemIsOre && !itemIsIngot;

                    // don't try sorting already sorted items
                    if (c % 3 == 0 && itemIsOre)
                    {
                        continue;
                    }
                    else if (c % 3 == 1 && itemIsIngot)
                    {
                        continue;
                    }
                    else if (c % 3 == 2 && itemIsComponent)
                    {
                        continue;
                    }

                    pushToStorage(container, 0, i, null);

                    // we don't check for success because we may end up sending stuff to
                    // the same container; rather, we check if volume has changed
                    if (vol != (float)inv.CurrentVolume)
                    {
                        n_sort++;
                        vol = (float)inv.CurrentVolume;
                    }
                    // sort ten items per run
                    if (n_sort == 10)
                    {
                        break;
                    }
                }
            }
        }

        // try pushing something to one of the local storage containers
        bool pushToStorage(IMyTerminalBlock b, int invIdx, int srcIndex, VRage.MyFixedPoint? amount)
        {
            var c = getStorage();
            /*
             * Stage 0: special case for small container numbers, or if sorting is
             * disabled. Basically, don't sort.
             */

            if (c.Count < 3 || !sort_storage)
            {
                foreach (var s in c)
                {
                    // try pushing to this container
                    if (Transfer(b, invIdx, s, 0, srcIndex, null, true, amount))
                    {
                        return true;
                    }
                }
                return false;
            }

            /*
             * Stage 1: try to put stuff into designated containers
             */
            var src = b.GetInventory(invIdx);
            var items = new List<MyInventoryItem>();
            src.GetItems(items);
            var item = items[srcIndex];
            bool itemIsOre = isOre(item);
            bool itemIsIngot = isIngot(item);
            bool itemIsComponent = !itemIsOre && !itemIsIngot;
            int startStep;
            if (itemIsOre)
            {
                startStep = 0;
            }
            else if (itemIsIngot)
            {
                startStep = 1;
            }
            else
            {
                startStep = 2;
            }
            int steps = 3;
            for (int i = startStep; i < c.Count; i += steps)
            {
                // try pushing to this container
                if (Transfer(b, invIdx, c[i], 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }

            /*
             * Stage 2: try to find a container we can overflow into. This can be either
             * a container we have previously overflown into, an empty container, or a
             * non-full container.
             */
            int overflowIdx = -1;
            int emptyIdx = -1;
            int leastFullIdx = -1;
            float maxAvail = 0;
            for (int i = 0; i < c.Count; i++)
            {
                // skip containers we already saw in a previous loop
                if (i % steps == startStep)
                {
                    continue;
                }

                // skip full containers
                var c_inv = c[i].GetInventory(0);
                float avail = ((float)c_inv.MaxVolume - (float)c_inv.CurrentVolume) * 1000;
                if (avail < 1)
                {
                    continue;
                }

                if (emptyIdx == -1 && (float)c_inv.CurrentVolume == 0)
                {
                    emptyIdx = i;
                    continue;
                }
                if (overflowIdx == -1)
                {
                    bool isOverflow = false;
                    if (itemIsOre && hasOnlyOre(c_inv))
                    {
                        isOverflow = true;
                    }
                    else if (itemIsIngot && hasOnlyIngots(c_inv))
                    {
                        isOverflow = true;
                    }
                    else if (itemIsComponent && hasOnlyComponents(c_inv))
                    {
                        isOverflow = true;
                    }
                    if (isOverflow)
                    {
                        overflowIdx = i;
                        continue;
                    }
                }
                if (avail > maxAvail)
                {
                    leastFullIdx = i;
                    maxAvail = avail;
                }
            }

            // now, try pushing into one of the containers we found
            if (overflowIdx != -1)
            {
                var dst = c[overflowIdx];
                if (Transfer(b, invIdx, dst, 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }
            if (emptyIdx != -1)
            {
                var dst = c[emptyIdx];
                if (Transfer(b, invIdx, dst, 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }
            if (leastFullIdx != -1)
            {
                var dst = c[leastFullIdx];
                if (Transfer(b, invIdx, dst, 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }
            return false;
        }

        // try pushing something to one of the remote storage containers
        bool pushToRemoteStorage(IMyTerminalBlock b, int srcInv, int srcIndex, VRage.MyFixedPoint? amount)
        {
            var s = getRemoteStorage();
            foreach (var c in s)
            {
                var c_inv = c.GetInventory(0);
                float avail = ((float)c_inv.MaxVolume - (float)c_inv.CurrentVolume) * 1000;
                if (avail < 1)
                {
                    continue;
                }
                // try pushing to this container
                if (Transfer(b, srcInv, c, 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }
            return false;
        }

        // try pushing something to one of the remote storage containers
        bool pushToRemoteShipStorage(IMyTerminalBlock b, int srcInv, int srcIndex, VRage.MyFixedPoint? amount)
        {
            var s = getRemoteShipStorage();
            foreach (var c in s)
            {
                // try pushing to this container
                if (Transfer(b, srcInv, c, 0, srcIndex, null, true, amount))
                {
                    return true;
                }
            }
            return false;
        }

        // send everything from local storage to remote storage
        void pushAllToRemoteStorage()
        {
            var s = getStorage();
            foreach (var c in s)
            {
                var inv = c.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && push_ore_to_base)
                    {
                        pushToRemoteStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var t = i.Type.SubtypeId;
                        if (t != U && push_ingots_to_base)
                        {
                            pushToRemoteStorage(c, 0, j, null);
                        }
                    }
                    if (isComponent(i) && push_components_to_base)
                    {
                        pushToRemoteStorage(c, 0, j, null);
                    }
                }
            }
        }

        // pull everything from remote storage
        void pullFromRemoteStorage()
        {
            if (isBaseMode())
            {
                return;
            }
            var s = getRemoteStorage();
            foreach (var c in s)
            {
                var items = new List<MyInventoryItem>();
                c.GetInventory(0).GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && pull_ore_from_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Type.SubtypeId;
                        // don't take all uranium from base
                        if (type == U && auto_refuel_ship && !powerAboveHighWatermark())
                        {
                            pushToStorage(c, 0, j, (VRage.MyFixedPoint)Math.Min(0.5F, (float)i.Amount));
                        }
                        else if (type != U && pull_ingots_from_base)
                        {
                            pushToStorage(c, 0, j, null);
                        }
                    }
                    if (isComponent(i) && pull_components_from_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                }
            }
        }

        // push everything to ship
        void pushToRemoteShipStorage()
        {
            var storage = getStorage();
            foreach (var s in storage)
            {
                var inv = s.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && pull_ore_from_base)
                    {
                        pushToRemoteShipStorage(s, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Type.SubtypeId;
                        if (type != U && pull_ingots_from_base)
                        {
                            pushToRemoteShipStorage(s, 0, j, null);
                        }
                    }
                    if (isComponent(i) && pull_components_from_base)
                    {
                        pushToRemoteShipStorage(s, 0, j, null);
                    }
                }
            }
        }

        // get everything from ship
        void pullFromRemoteShipStorage()
        {
            var s = getRemoteShipStorage();
            foreach (var c in s)
            {
                var inv = c.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && push_ore_to_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Type.SubtypeId;
                        // don't take all uranium from base
                        if (type != U && push_ingots_to_base)
                        {
                            pushToStorage(c, 0, j, null);
                        }
                    }
                    if (isComponent(i) && push_components_to_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                }
            }
        }

        // push everything in every block to local storage
        void emptyBlocks(List<IMyTerminalBlock> bs)
        {
            foreach (var b in bs)
            {
                var inv = b.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    pushToStorage(b, 0, j, null);
                }
            }
        }

        // push stuff to welders
        void fillWelders()
        {
            var welders = getWelders();
            int s_index = 0;

            foreach (var w in welders)
            {
                float cur_vol = (float)w.GetInventory(0).CurrentVolume * 1000;
                float max_vol = (float)w.GetInventory(0).MaxVolume * 1000;
                float target = max_vol - 400 - cur_vol;
                if (target <= 0)
                {
                    continue;
                }
                var dst_inv = w.GetInventory(0);
                var s = getStorage();
                for (; s_index < s.Count; s_index++)
                {
                    var c = s[s_index];
                    var src_inv = c.GetInventory(0);
                    var items = new List<MyInventoryItem>();
                    src_inv.GetItems(items);
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        var i = items[j];
                        if (!isComponent(i))
                        {
                            continue;
                        }
                        if (target <= 0)
                        {
                            break;
                        }
                        float amount = (float)i.Amount - 1;
                        // if it's peanuts, just send out everthing
                        if (amount < 2)
                        {
                            src_inv.TransferItemTo(dst_inv, j, null, true, null);
                            continue;
                        }

                        // send one and check load
                        float old_vol = (float)dst_inv.CurrentVolume * 1000;
                        if (!Transfer(c, 0, w, 0, j, null, true, (VRage.MyFixedPoint)1))
                        {
                            continue;
                        }
                        float new_vol = (float)dst_inv.CurrentVolume * 1000;
                        float item_vol = new_vol - old_vol;
                        int missing = (int)Math.Floor(target / item_vol);
                        src_inv.TransferItemTo(dst_inv, j, null, true, (VRage.MyFixedPoint)missing);
                        target -= (float)Math.Min(missing, amount) * item_vol;
                    }
                    if (target <= 0)
                    {
                        break;
                    }
                }
            }
        }

        // push all ore from refineries to storage
        void pushOreToStorage()
        {
            var rs = getRefineries();
            foreach (var r in rs)
            {
                var inv = r.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    pushToStorage(r, 0, j, null);
                }
            }
        }

        // push ice from refineries to storage (we never push ice from oxygen generators)
        void pushIceToStorage()
        {
            var rs = getRefineries();
            foreach (var r in rs)
            {
                var inv = r.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (i.Type.SubtypeId != ICE)
                    {
                        continue;
                    }
                    pushToStorage(r, 0, j, null);
                }
            }
        }
        #endregion

        #region POWER
        /**
         * Uranium, reactors & batteries
         */
        // get total amount of stored power
        float getStoredPower(bool force_update = false)
        {
            if (!force_update)
            {
                return cur_stored_power;
            }
            float stored = 0;

            // go through batteries
            foreach (IMyBatteryBlock b in getBatteries())
            {
                // unlike reactors, batteries' kWh are _actual_ kWh, not kWm
                stored += b.CurrentStoredPower * 1000 * 60;
            }

            // reactors store power in uranium, but it's only stored
            // power if you have a reactor in the first place
            if (has_reactors)
            {
                stored += URANIUM_INGOT_POWER * ingot_status[U];
            }

            // hydrogen engines do have stored power, but the won't be
            // included in this calculation for now
            cur_stored_power = stored;

            return stored;
        }

        float getTransientPower(bool force_update = false)
        {
            if (!force_update)
            {
                return cur_transient_power;
            }
            float cur_output = 0;

            // go through power-producing blocks but exclude
            // batteries and reactors
            foreach (IMyPowerProducer p in getPowerProducers())
            {
                if (p is IMyBatteryBlock || p is IMyReactor)
                    continue;
                cur_output += p.CurrentOutput * 1000;
            }
            cur_transient_power = cur_output;

            return cur_transient_power;
        }

        float getMaxPowerOutput(bool force_update = false)
        {
            if (!force_update)
            {
                return max_power_output;
            }

            float output = 0;

            foreach (IMyPowerProducer p in getPowerProducers())
            {
                // don't count disabled blocks
                if (!p.Enabled)
                    continue;

                // don't count batteries that are recharging
                var b = p as IMyBatteryBlock;
                if (b != null && b.ChargeMode == ChargeMode.Recharge)
                {
                    continue;
                }
                output += p.MaxOutput * 1000;
            }

            max_power_output = output;

            return max_power_output;
        }
        
        float getCurPowerDraw(bool force_update = false)
        {
            if (!force_update)
            {
                return cur_power_draw;
            }

            float power_draw = 0;

            // the API doesn't expose current power usage for
            // all blocks, so instead we'll use current power output
            // from all of the producers

            foreach (IMyPowerProducer p in getPowerProducers())
            {
                power_draw += p.CurrentOutput * 1000;
                // batteries are a special case because they have input as well
                var b = p as IMyBatteryBlock;
                if (b != null)
                    power_draw -= b.CurrentInput * 1000;
            }
            cur_power_draw = power_draw;

            return cur_power_draw;
        }

        float getMaxPowerDraw(bool force_update = false)
        {
            if (!force_update)
            {
                return max_power_draw;
            }

            float power_draw = 0;

            // go through all the blocks
            foreach (var b in getBlocks())
            {
                if (b is IMyBatteryBlock)
                    continue;
                power_draw += getBlockPowerUse(b);
            }
            // add 10% to account for various misc stuff like conveyors etc
            max_power_draw = power_draw * 1.1F;

            return max_power_draw;
        }

        float getBlockPowerUse(IMyTerminalBlock b)
        {
            if (b is IMyThrust)
            {
                var typename = b.BlockDefinition.SubtypeName;
                float thrust_draw;
                if (thrust_power.TryGetValue(typename, out thrust_draw))
                {
                    return thrust_draw;
                }
            }
            MyDefinitionId ElectricityId = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Electricity");

            var sink = b.Components.Get<MyResourceSinkComponent>();
            if (sink != null && sink.AcceptedResources.Contains(ElectricityId))
            {
                return sink.MaxRequiredInputByType(ElectricityId) * 1000F;
            }
            // we couldn't get block's power use
            return 0;
        }

        float getPowerHighWatermark(float power_use)
        {
            return power_use * pwr_hi_wm;
        }

        float getPowerLowWatermark(float power_use)
        {
            return power_use * pwr_lo_wm;
        }

        bool powerAboveHighWatermark()
        {
            var stored_power = getStoredPower();

            // check if we have enough uranium ingots to fill all local reactors and
            // have a few spare ones
            float power_draw;
            if (isShipMode() && connected_to_base)
            {
                power_draw = getMaxPowerDraw();
            }
            else
            {
                power_draw = getCurPowerDraw();
            }
            float power_needed = getPowerHighWatermark(power_draw);
            float power_thresh = power_needed * 1.3F;

            if (stored_power > power_thresh)
            {
                power_above_threshold = true;
                return true;
            }
            // if we always go by fixed limit, we will constantly have to refine uranium.
            // therefore, rather than constantly refining uranium, let's watch a certain
            // threshold and allow for other ore to be refined while we still have lots of
            // spare uranium
            if (stored_power > power_needed && power_above_threshold)
            {
                return true;
            }
            // we flip the switch, so next time we decide it's time to leave uranium alone
            // will be when we have uranium above threshold
            power_above_threshold = false;

            return false;
        }

        // check if we don't have much power left
        bool powerAboveLowWatermark()
        {
            float power_draw;
            if (isShipMode() && connected_to_base)
            {
                power_draw = getMaxPowerDraw();
            }
            else
            {
                power_draw = getCurPowerDraw();
            }
            return getStoredPower() > getPowerLowWatermark(power_draw);
        }

        // push uranium into reactors, optionally push ALL uranium into reactors
        bool refillReactors(bool force = false)
        {
            bool refilled = true;
            ItemHelper ingot = null;
            float orig_amount = 0, cur_amount = 0;
            int s_index = 0;
            // check if we can put some more uranium into reactors
            var rs = getReactors();
            foreach (IMyReactor r in rs)
            {
                var rinv = r.GetInventory(0);
                // always assume 100% power load for reactors
                float r_power_draw = r.MaxOutput * 1000;
                float ingots_per_reactor = getPowerHighWatermark(r_power_draw) / URANIUM_INGOT_POWER;
                float ingots_in_reactor = getTotalIngots(r, 0, U);
                if ((ingots_in_reactor < ingots_per_reactor) || force)
                {
                    // find us an ingot
                    if (ingot == null)
                    {
                        var storage = getStorage();
                        for (; s_index < storage.Count; s_index++)
                        {
                            var sinv = storage[s_index].GetInventory(0);
                            var items = new List<MyInventoryItem>();
                            sinv.GetItems(items);
                            for (int j = 0; j < items.Count; j++)
                            {
                                var item = items[j];
                                if (isIngot(item) && item.Type.SubtypeId == U)
                                {
                                    ingot = new ItemHelper();
                                    ingot.InvIdx = 0;
                                    ingot.Index = j;
                                    ingot.Item = item;
                                    ingot.Owner = storage[s_index];
                                    orig_amount = (float)item.Amount;
                                    cur_amount = orig_amount;
                                    break;
                                }
                            }
                            if (ingot != null)
                            {
                                break;
                            }
                        }
                        // if we didn't find any ingots
                        if (ingot == null)
                        {
                            return false;
                        }
                    }
                    float amount = orig_amount;
                    if (!force)
                    {
                        amount = (float)Math.Min(amount, ingots_per_reactor - ingots_in_reactor);
                    }

                    // don't leave change, we've expended this ingot
                    if (cur_amount - amount <= 0.05F)
                    {
                        cur_amount = 0;
                        rinv.TransferItemFrom(ingot.Owner.GetInventory(ingot.InvIdx), ingot.Index, null, true, null);
                        ingot = null;
                    }
                    else
                    {
                        amount = TryTransfer(ingot.Owner, ingot.InvIdx, r, 0, ingot.Index, null, true, (VRage.MyFixedPoint)amount);
                        if (amount > 0)
                        {
                            cur_amount -= amount;
                        }
                    }
                    if (ingots_in_reactor + amount < ingots_per_reactor)
                    {
                        refilled = false;
                    }
                }
            }
            return refilled;
        }

        // push uranium to storage if we have too much of it in reactors
        void pushSpareUraniumToStorage()
        {
            var rs = getReactors();
            foreach (IMyReactor r in rs)
            {
                var inv = r.GetInventory(0);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                if (items.Count > 1)
                {
                    consolidate(inv);
                }
                float ingots = getTotalIngots(r, 0, U);
                float power_draw = r.MaxOutput * 1000;
                float ingots_per_reactor = getPowerHighWatermark(power_draw) / URANIUM_INGOT_POWER;
                if (ingots > ingots_per_reactor)
                {
                    float amount = ingots - ingots_per_reactor;
                    pushToStorage(r, 0, 0, (VRage.MyFixedPoint)amount);
                }
            }
        }
        #endregion

        #region TRASH
        /**
         * Trash
         */
        bool startThrowing(IMyShipConnector connector, bool force = false)
        {
            // if connector is locked, it's in use, so don't do anything
            if (!connector.Enabled)
            {
                return false;
            }
            if (connector.Status != MyShipConnectorStatus.Unconnected)
            {
                return false;
            }
            if (!force && hasUsefulItems(connector))
            {
                return false;
            }
            connector.ThrowOut = true;

            return true;
        }

        void stopThrowing(IMyShipConnector connector)
        {
            // at this point, we have already turned off the conveyors,
            // so now just check if we are throwing and if we are in
            // "collect all" mode
            connector.CollectAll = false;
            connector.ThrowOut = false;
        }

        void startThrowing(bool force = false)
        {
            foreach (IMyShipConnector connector in getTrashConnectors())
            {
                startThrowing(connector, force);
            }
        }

        void stopThrowing()
        {
            foreach (IMyShipConnector connector in getTrashConnectors())
            {
                stopThrowing(connector);
            }
        }

        // only gravel is considered unuseful, and only if config says to throw out stone
        bool isUseful(MyInventoryItem item)
        {
            return !throw_out_gravel || !isIngot(item) || item.Type.SubtypeId != STONE;
        }

        bool hasUsefulItems(IMyShipConnector connector)
        {
            var inv = connector.GetInventory(0);
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            foreach (var item in items)
            {
                if (isUseful(item))
                {
                    return true;
                }
            }
            return false;
        }

        bool trashHasUsefulItems()
        {
            foreach (IMyShipConnector connector in getTrashConnectors())
            {
                if (hasUsefulItems(connector))
                {
                    return true;
                }
            }
            return false;
        }

        bool trashSensorsActive()
        {
            var sensors = getTrashSensors();
            foreach (IMySensorBlock sensor in sensors)
            {
                if (sensor.IsActive)
                {
                    return true;
                }
            }
            return false;
        }

        bool throwOutOre(string name, float ore_amount = 0, bool force = false)
        {
            var connectors = getTrashConnectors();
            var skip_list = new List<IMyTerminalBlock>();

            if (connectors.Count == 0)
            {
                return false;
            }
            float orig_target = ore_amount == 0 ? 5 * CHUNK_SIZE : ore_amount;
            float target_amount = orig_target;

            // first, go through list of connectors and enable throw
            foreach (IMyShipConnector connector in connectors)
            {
                if (!startThrowing(connector, force))
                {
                    skip_list.Add(connector);
                    continue;
                }
            }

            // now, find all instances of ore we're looking for, and push it to trash
            var entries = getAllStorageOre(name);
            foreach (var entry in entries)
            {
                var item = entry.Item;
                var srcObj = entry.Owner;
                var invIdx = entry.InvIdx;
                var index = entry.Index;
                var orig_amount = (float)Math.Min(target_amount, (float)entry.Item.Amount);
                var cur_amount = orig_amount;

                foreach (IMyShipConnector connector in connectors)
                {
                    if (skip_list.Contains(connector))
                    {
                        continue;
                    }
                    var amount = (float)Math.Min(orig_amount / getTrashConnectors().Count, cur_amount);

                    // send it to connector
                    var transferred = TryTransfer(srcObj, invIdx, connector, 0, index, null, true,
                                                    (VRage.MyFixedPoint)amount);
                    if (transferred > 0)
                    {
                        target_amount -= transferred;
                        cur_amount -= transferred;

                        if (target_amount == 0)
                        {
                            return true;
                        }
                        if (cur_amount == 0)
                        {
                            break;
                        }
                    }
                }
            }
            return target_amount != orig_target || entries.Count == 0;
        }

        bool throwOutIngots(string name, float ingot_amount = 0, bool force = false)
        {
            var connectors = getTrashConnectors();
            var skip_list = new List<IMyTerminalBlock>();

            if (connectors.Count == 0)
            {
                return false;
            }
            float orig_target = ingot_amount == 0 ? 5 * CHUNK_SIZE : ingot_amount;
            float target_amount = orig_target;

            // first, go through list of connectors and enable throw
            foreach (IMyShipConnector connector in connectors)
            {
                if (!startThrowing(connector, force))
                {
                    skip_list.Add(connector);
                    continue;
                }
            }

            // now, find all instances of ore we're looking for, and push it to trash
            var entries = getAllStorageIngots(name);
            foreach (var entry in entries)
            {
                var item = entry.Item;
                var srcObj = entry.Owner;
                var invIdx = entry.InvIdx;
                var index = entry.Index;
                var orig_amount = (float)Math.Min(target_amount, (float)entry.Item.Amount);
                var cur_amount = orig_amount;

                foreach (IMyShipConnector connector in connectors)
                {
                    if (skip_list.Contains(connector))
                    {
                        continue;
                    }
                    var amount = (float)Math.Min(orig_amount / getTrashConnectors().Count, cur_amount);

                    // send it to connector
                    var transferred = TryTransfer(srcObj, invIdx, connector, 0, index, null, true,
                                                    (VRage.MyFixedPoint)amount);
                    if (transferred > 0)
                    {
                        target_amount -= transferred;
                        cur_amount -= transferred;

                        if (target_amount == 0)
                        {
                            return true;
                        }
                        if (cur_amount == 0)
                        {
                            break;
                        }
                    }
                }
            }
            return target_amount != orig_target || entries.Count == 0;
        }

        // move everything (or everything excluding gravel) from trash to storage
        void storeTrash(bool store_all = false)
        {
            int count = 0;
            if (store_all || trashHasUsefulItems())
            {
                stopThrowing();
            }
            var connectors = getTrashConnectors();
            foreach (var connector in connectors)
            {
                var inv = connector.GetInventory(0);
                consolidate(inv);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    if (!store_all && !isUseful(item))
                    {
                        continue;
                    }
                    // do this ten times
                    if (pushToStorage(connector, 0, i, null) && ++count == 10)
                    {
                        return;
                    }
                }
            }
        }
        #endregion

        #region ORE
        /**
         * Ore and refineries
         */
        void refineOre()
        {
            refine_ice = !iceAboveHighWatermark();
            var items = getAllStorageOre();
            foreach (var item in items)
            {
                List<IMyTerminalBlock> rs;
                string ore = item.Item.Type.SubtypeId;
                if (ore == SCRAP)
                {
                    ore = FE;
                }
                // ice is preferably refined with oxygen generators, and only if we need to
                if (ore == ICE)
                {
                    rs = getOxygenGenerators();
                    if (rs.Count == 0)
                    {
                        if (!refine_ice)
                        {
                            continue;
                        }
                        rs = getRefineries();
                    }
                }
                else
                {
                    // get refineries and filter out anything that doesn't accept this ore
                    rs = new List<IMyTerminalBlock>(getRefineries());
                    rs.RemoveAll(b => !block_refine_map[getBlockDefinitionStr(b)].Contains(ore));
                }
                if (rs.Count == 0)
                {
                    continue;
                }

                float orig_amount = (float)Math.Round((float)item.Item.Amount / rs.Count, 4);
                float amount = (float)Math.Min(CHUNK_SIZE, orig_amount);
                // now, go through every refinery and do the transfer
                for (int ri = 0; ri < rs.Count; ri++)
                {
                    // if we're last in the list, send it all
                    if (ri == rs.Count - 1 && amount < CHUNK_SIZE)
                    {
                        amount = 0;
                    }
                    var r = rs[ri];
                    removeBlockAlert(r, ALERT_CLOGGED);
                    var in_inv = r.GetInventory(0);
                    var out_inv = r.GetInventory(1);
                    float input_load = (float)in_inv.CurrentVolume / (float)in_inv.MaxVolume;
                    if (canAcceptOre(in_inv, ore) || ore == ICE)
                    {
                        // if we've got a very small amount, send it all
                        var item_inv = item.Owner.GetInventory(item.InvIdx);
                        if (amount < 1)
                        {
                            var i = new List<MyInventoryItem>();
                            in_inv.GetItems(i);
                            if (Transfer(item.Owner, item.InvIdx, r, 0, item.Index, i.Count, true, null))
                            {
                                break;
                            }
                        }
                        // if refinery is almost empty, send a lot
                        else if (input_load < 0.2F)
                        {
                            amount = (float)Math.Min(CHUNK_SIZE * 5, orig_amount);
                            var i = new List<MyInventoryItem>();
                            in_inv.GetItems(i);
                            item_inv.TransferItemTo(in_inv, item.Index, i.Count, true, (VRage.MyFixedPoint)amount);
                        }
                        else
                        {
                            var i = new List<MyInventoryItem>();
                            in_inv.GetItems(i);
                            item_inv.TransferItemTo(in_inv, item.Index, i.Count, true, (VRage.MyFixedPoint)amount);
                        }
                    }
                }
            }
        }

        // find which ore needs prioritization the most
        void reprioritizeOre()
        {
            var lo_wm_ores = new List<string>();
            var hi_wm_ores = new List<string>();

            // find us ore to prioritize
            foreach (var ore in ore_types)
            {
                if (ore == ICE)
                {
                    // ice is refined separately
                    continue;
                }
                bool has_ore = ore_status[ore] > 0;
                bool isLoWm = storage_ingot_status[ore] < (material_thresholds[ore] * material_threshold_multiplier);
                bool isHiWm = storage_ingot_status[ore] < (material_thresholds[ore] * 5 * material_threshold_multiplier) && ore_status[ore] > 0;
                // if there's ore needed by assemblers, override priority
                bool @override = assembler_ingots.Contains(ore);

                // do we have this ore?
                if (!has_ore && (isLoWm || isHiWm))
                {
                    // there is shortage of this material, but there is no ore of
                    // this kind. however, some ores are mined with stone as well,
                    // so check if we can prioritize stone instead
                    if (!ore_to_ingot_ratios[STONE].ContainsKey(ore))
                        continue;

                    // this is an ore that we can get from stone, so check if we have stone
                    if (ore_status[STONE] < 1)
                        continue;

                    // great, we have stone! we don't have to check if we already
                    // prioritized it, because we'll only pick one ore from the list
                    // anyway. now we know we can prioritize stone, but do we need
                    // to prioritize it above all else?
                    if (@override && isLoWm)
                        lo_wm_ores.Insert(0, STONE);
                    else if (@override && isHiWm)
                        hi_wm_ores.Insert(0, STONE);
                    else if (isLoWm)
                        lo_wm_ores.Add(STONE);
                    else if (isHiWm)
                        hi_wm_ores.Add(STONE);

                    continue;
                }
                // we have this ore! now, check how we should prioritize it

                // we're overriding the priority, so we need to put it at the top
                // of the list of ores to refine
                if (isLoWm && @override)
                {
                    lo_wm_ores.Insert(0, ore);
                } else if (isHiWm && @override)
                {
                    hi_wm_ores.Insert(0, ore);
                }
                else if (has_ore && isLoWm)
                {
                    lo_wm_ores.Add(ore);
                }
                else if (isHiWm)
                {
                    hi_wm_ores.Add(ore);
                }
            }

            // if we know we want uranium, prioritize it above all else
            if (prioritize_uranium && ore_status[U] > 0)
            {
                lo_wm_ores.Insert(0, U);
            }
            // now, reorder ore in refineries
            if (hi_wm_ores.Count != 0 || lo_wm_ores.Count != 0)
            {
                var rs = getRefineries();
                foreach (var r in rs)
                {
                    // ores are in priority order, and not all refineries accept all ores,
                    // so we need to filter out anything that cannot be refined here
                    string ore = null;
                    string bd = getBlockDefinitionStr(r);
                    foreach (var o in lo_wm_ores)
                    {
                        if (!block_refine_map[bd].Contains(o))
                            continue;
                        ore = o;
                        break;
                    }
                    if (ore == null)
                    {
                        foreach (var o in hi_wm_ores)
                        {
                            if (!block_refine_map[bd].Contains(o))
                                continue;
                            ore = o;
                            break;
                        }
                    }
                    if (ore == null)
                    {
                        // this refinery cannot prioritize any ores that are needed
                        continue;
                    }

                    var inv = r.GetInventory(0);
                    var items = new List<MyInventoryItem>();
                    inv.GetItems(items);
                    for (int j = 0; j < items.Count; j++)
                    {
                        var cur = items[j].Type.SubtypeId;
                        if (cur == SCRAP)
                        {
                            cur = FE;
                        }
                        if (cur != ore)
                        {
                            continue;
                        }
                        pushFront(inv, j, items[j].Amount);
                        break;
                    }
                }
            }
        }

        /*
         * Rebalancing code
         *
         * Keep out unless you know what you're doing...
         */
        public class RebalanceResult
        {
            public int minIdx;
            public int maxIdx;
            public float minLoad;
            public float maxLoad;
        }

        // go through a list of blocks and find most and least utilized
        RebalanceResult findMinMax(List<IMyTerminalBlock> blocks)
        {
            var r = new RebalanceResult();
            int minI = 0, maxI = 0;
            float minL = float.MaxValue, maxL = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                var inv = b.GetInventory(0);
                rebalance(inv);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                foreach (var item in items)
                {
                    var name = item.Type.SubtypeId;
                    if (name == SCRAP)
                    {
                        name = FE;
                    }
                }
                float load = (float)inv.CurrentVolume * 1000;

                if (load < minL)
                {
                    minI = i;
                    minL = load;
                }
                if (load > maxL)
                {
                    maxI = i;
                    maxL = load;
                }
            }
            r.minIdx = minI;
            r.maxIdx = maxI;
            r.minLoad = minL;
            r.maxLoad = maxL;
            return r;
        }

        // spread ore between two inventories
        bool spreadOre(IMyTerminalBlock src, int srcIdx, IMyTerminalBlock dst, int dstIdx)
        {
            bool success = false;

            var src_inv = src.GetInventory(srcIdx);
            var dst_inv = dst.GetInventory(dstIdx);
            var maxL = (float)src_inv.CurrentVolume * 1000;
            var minL = (float)dst_inv.CurrentVolume * 1000;

            var items = new List<MyInventoryItem>();
            src_inv.GetItems(items);
            var target_vol = (float)(maxL - minL) / 2;
            // spread all ore equally
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var vol = items[i].Type.SubtypeId == SCRAP ? VOLUME_SCRAP : VOLUME_ORE;
                var cur_amt = (float)items[i].Amount;
                var cur_vol = (cur_amt * vol) / 2;
                float amt = (float)Math.Min(target_vol, cur_vol) / vol;
                VRage.MyFixedPoint? tmp = (VRage.MyFixedPoint)amt;
                // if there's peanuts, send it all
                if (cur_amt < 250)
                {
                    tmp = null;
                    amt = (float)items[i].Amount;
                }
                amt = TryTransfer(src, srcIdx, dst, dstIdx, i, null, true, tmp);
                if (amt > 0)
                {
                    success = true;
                    target_vol -= amt * vol;
                    if (target_vol <= 0)
                    {
                        break;
                    }
                }
            }
            if (success)
            {
                rebalance(src_inv);
                rebalance(dst_inv);
            }
            return success;
        }

        // go through all blocks capable of refining ore, and find
        // least and most utilized, and spread load between them
        void rebalanceRefineries()
        {
            var ratio = 1.25F;

            // go through all sets of ores supported by each refining block,
            // and rebalance between all blocks that support these ores
            foreach (var s in block_refine_map.Values)
            {
                var bs = new List<IMyTerminalBlock>();

                // special case: for ice, we only consider oxygen generators unless
                // we don't have any
                if (s.Count == 1 && s.Contains(ICE) && getOxygenGenerators().Count > 0)
                {
                    bs.AddList(getOxygenGenerators());
                }
                else
                {
                    foreach (var b in getRefineries())
                    {
                        // check if this block can refine all ores from current set
                        if (block_refine_map[getBlockDefinitionStr(b)].IsSubsetOf(s))
                        {
                            bs.Add(b);
                        }
                    }
                }

                // we now have all blocks that support current subset of ores
                // now we can rebalance between all of these blocks

                var res = findMinMax(bs);

                if (res.maxLoad > 250)
                {
                    bool trySpread = res.minLoad == 0 || res.maxLoad / res.minLoad > ratio;
                    if (res.minIdx != res.maxIdx && trySpread)
                    {
                        var src = bs[res.maxIdx];
                        var dst = bs[res.minIdx];
                        spreadOre(src, 0, dst, 0);
                    }
                }
            }
        }

        // find ore of which we have the most amount of
        string getBiggestOre()
        {
            float max = 0;
            string name = "";
            foreach (var ore in ore_types)
            {
                // skip uranium
                if (ore == U)
                {
                    continue;
                }
                var amount = ore_status[ore];
                if (amount > max)
                {
                    name = ore;
                    max = amount;
                }
            }
            // if we're out of ore
            if (max == 0)
            {
                return null;
            }
            return name;
        }

        // check if refineries can refine
        bool refineriesClogged()
        {
            var rs = getRefineries();
            foreach (IMyRefinery r in rs)
            {
                if (r.IsQueueEmpty || r.IsProducing)
                {
                    return false;
                }
            }
            return true;
        }
        #endregion

        #region DECLOG
        /**
         * Declog and spread load
         */
        void declogAssemblers()
        {
            var @as = getAssemblers();
            foreach (IMyAssembler a in @as)
            {
                var inv = a.GetInventory(0);

                // empty assembler input if it's not doing anything
                var items = new List<MyInventoryItem>();
                if (a.IsQueueEmpty)
                {
                    inv.GetItems(items);
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        pushToStorage(a, 0, j, null);
                    }
                }
                items.Clear();

                inv = a.GetInventory(1);

                // empty output but only if it's not disassembling
                if (a.Mode != MyAssemblerMode.Disassembly)
                {
                    inv.GetItems(items);
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        pushToStorage(a, 1, j, null);
                    }
                }
            }
        }

        void declogRefineries()
        {
            var rs = getRefineries();
            foreach (var r in rs)
            {
                var inv = r.GetInventory(1);
                var items = new List<MyInventoryItem>();
                inv.GetItems(items);
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    pushToStorage(r, 1, j, null);
                }
            }
        }

        // find least and most utilized block, and spread load between them (used for
        // drills, grinders and welders)
        void spreadLoad(List<IMyTerminalBlock> blocks)
        {
            float minLoad = 100, maxLoad = 0;
            float minVol = 0, maxVol = 0;
            int minIdx = 0, maxIdx = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                float load = (float)blocks[i].GetInventory(0).CurrentVolume / (float)blocks[i].GetInventory(0).MaxVolume;
                if (load < minLoad)
                {
                    minIdx = i;
                    minLoad = load;
                    minVol = (float)blocks[i].GetInventory(0).CurrentVolume * 1000;
                }
                if (load > maxLoad)
                {
                    maxIdx = i;
                    maxLoad = load;
                    maxVol = (float)blocks[i].GetInventory(0).CurrentVolume * 1000;
                }
            }
            // even out the load between biggest loaded block
            if (minIdx != maxIdx && (minLoad == 0 || maxLoad / minLoad > 1.1F))
            {
                var src = blocks[maxIdx];
                var dst = blocks[minIdx];
                var src_inv = blocks[maxIdx].GetInventory(0);
                var dst_inv = blocks[minIdx].GetInventory(0);
                var target_vol = (maxVol - minVol) / 2;
                var items = new List<MyInventoryItem>();
                src_inv.GetItems(items);
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (target_vol <= 0)
                    {
                        return;
                    }
                    float amt = (float)items[i].Amount - 1;
                    // if it's peanuts, just send out everything
                    if (amt < 1)
                    {
                        src_inv.TransferItemTo(dst_inv, i, null, true, null);
                        continue;
                    }

                    // send one and check load
                    float cur_vol = (float)dst_inv.CurrentVolume * 1000;
                    if (!Transfer(src, 0, dst, 0, i, null, true, (VRage.MyFixedPoint)1))
                    {
                        continue;
                    }
                    float new_vol = (float)dst_inv.CurrentVolume * 1000;
                    float item_vol = new_vol - cur_vol;
                    int target_amt = (int)Math.Floor(target_vol / item_vol);
                    src_inv.TransferItemTo(dst_inv, i, null, true, (VRage.MyFixedPoint)target_amt);
                    target_vol -= (float)Math.Min(target_amt, amt) * item_vol;
                }
            }
        }
        #endregion

        #region ICE
        /**
         * Oxygen
         */
        void checkOxygenLeaks()
        {
            var bs = getAirVents();
            bool alert = false;
            foreach (IMyAirVent v in bs)
            {
                if (v.Status == VentStatus.Pressurizing && !v.CanPressurize)
                {
                    addBlockAlert(v, ALERT_OXYGEN_LEAK);
                    alert = true;
                }
                else
                {
                    removeBlockAlert(v, ALERT_OXYGEN_LEAK);
                }
                updateBlockName(v);
            }
            if (alert)
            {
                addAlert(AlertLevel.ALERT_BROWN);
            }
            else
            {
                removeAlert(AlertLevel.ALERT_BROWN);
            }
        }

        void toggleOxygenGenerators(bool val)
        {
            var rs = getOxygenGenerators();
            foreach (IMyGasGenerator r in rs)
            {
                r.Enabled = val;
            }
        }

        void toggleStockpile(List<IMyTerminalBlock> blocks, bool val)
        {
            foreach (IMyGasTank b in blocks)
            {
                b.Stockpile = val;
            }
        }

        bool oxygenAboveHighWatermark()
        {
            float cur_oxygen_level = getStoredOxygen() / getOxygenCapacity() * 100;
            if (has_oxygen_tanks && o2_hi_wm > 0 && cur_oxygen_level < o2_hi_wm)
            {
                return false;
            }
            return true;
        }

        bool oxygenAboveLowWatermark()
        {
            float cur_oxygen_level = getStoredOxygen() / getOxygenCapacity() * 100;
            if (has_oxygen_tanks && o2_lo_wm > 0 && cur_oxygen_level < o2_lo_wm)
            {
                return false;
            }
            return true;
        }

        bool hydrogenAboveHighWatermark()
        {
            float cur_hydrogen_level = getStoredHydrogen() / getHydrogenCapacity() * 100;
            if (has_hydrogen_tanks && h2_hi_wm > 0 && cur_hydrogen_level < h2_hi_wm)
            {
                return false;
            }
            return true;
        }

        bool hydrogenAboveLowWatermark()
        {
            float cur_hydrogen_level = getStoredHydrogen() / getHydrogenCapacity() * 100;
            if (has_hydrogen_tanks && h2_lo_wm > 0 && cur_hydrogen_level < h2_lo_wm)
            {
                return false;
            }
            return true;
        }

        bool iceAboveHighWatermark()
        {
            return oxygenAboveHighWatermark() && hydrogenAboveHighWatermark();
        }

        float getOxygenCapacity(bool forced_update = false)
        {
            if (!forced_update)
            {
                return cur_oxygen_capacity;
            }
            int n_oxygen_tanks = getOxygenTanks().Count;
            float capacity = large_grid ? LARGE_O2_TANK_CAPACITY : SMALL_O2_TANK_CAPACITY;
            cur_oxygen_capacity = capacity * n_oxygen_tanks;

            return cur_oxygen_capacity;
        }

        float getHydrogenCapacity(bool forced_update = false)
        {
            if (!forced_update)
            {
                return cur_hydrogen_capacity;
            }
            int n_hydrogen_tanks = getHydrogenTanks().Count;
            float capacity = large_grid ? LARGE_H2_TANK_CAPACITY : SMALL_H2_TANK_CAPACITY;
            cur_hydrogen_capacity = capacity * n_hydrogen_tanks;

            return cur_hydrogen_capacity;
        }

        float getStoredOxygen(bool forced_update = false)
        {
            if (!forced_update)
            {
                return cur_stored_oxygen;
            }
            float cur = 0;
            float max = 0;
            foreach (IMyGasTank tank in getOxygenTanks())
            {
                cur += (float)tank.FilledRatio;
                max += 1;
            }

            cur_stored_oxygen = getOxygenCapacity() * (cur / max);

            return cur_stored_oxygen;
        }

        float getStoredHydrogen(bool forced_update = false)
        {
            if (!forced_update)
            {
                return cur_stored_hydrogen;
            }
            float cur = 0;
            float max = 0;
            foreach (IMyGasTank tank in getHydrogenTanks())
            {
                cur += (float)tank.FilledRatio;
                max += 1;
            }

            cur_stored_hydrogen = getHydrogenCapacity() * (cur / max);

            return cur_stored_hydrogen;
        }

        float getOxygenInIce()
        {
            if (!can_refine_ice)
            {
                return 0;
            }
            return ingot_status[ICE] * ICE_TO_GAS_RATIO;
        }

        float getHydrogenInIce()
        {
            if (!can_refine_ice)
            {
                return 0;
            }
            return ingot_status[ICE] * ICE_TO_GAS_RATIO;
        }
        #endregion

        #region CONFIG
        /**
         * Functions pertaining to BARABAS's operation
         */
        bool isShipMode()
        {
            return (op_mode & OP_MODE_SHIP) != 0;
        }

        bool isGenericShipMode()
        {
            return op_mode == OP_MODE_SHIP;
        }

        // tug is not considered a specialized ship
        bool isSpecializedShipMode()
        {
            return (op_mode & (OP_MODE_DRILL | OP_MODE_WELDER | OP_MODE_GRINDER)) != 0;
        }

        bool isBaseMode()
        {
            return op_mode == OP_MODE_BASE;
        }

        bool isDrillMode()
        {
            return (op_mode & OP_MODE_DRILL) != 0;
        }

        bool isWelderMode()
        {
            return (op_mode & OP_MODE_WELDER) != 0;
        }

        bool isGrinderMode()
        {
            return (op_mode & OP_MODE_GRINDER) != 0;
        }

        bool isTugMode()
        {
            return (op_mode & OP_MODE_TUG) != 0;
        }

        bool isAutoMode()
        {
            return op_mode == OP_MODE_AUTO;
        }

        void setMode(int mode)
        {
            switch (mode)
            {
                case OP_MODE_AUTO:
                case OP_MODE_BASE:
                case OP_MODE_SHIP:
                    break;
                case OP_MODE_DRILL:
                case OP_MODE_WELDER:
                case OP_MODE_GRINDER:
                case OP_MODE_TUG:
                    mode |= OP_MODE_SHIP;
                    break;
                default:
                    throw new BarabasException("Invalid operation mode specified", this);
            }
            op_mode = mode;
        }

        void resetConfig()
        {
            trigger_mode = false;
            update_period = 6;
            hud_notifications = true;
            sort_storage = false;
            pull_ore_from_base = false;
            pull_ingots_from_base = false;
            pull_components_from_base = false;
            push_ore_to_base = false;
            push_ingots_to_base = false;
            push_components_to_base = false;
            refuel_oxygen = false;
            refuel_hydrogen = false;
            throw_out_gravel = true;
            material_thresholds[STONE] = 0; // by default, keep all gravel
            pwr_lo_wm = 0;
            pwr_hi_wm = 0;
            o2_hi_wm = 0;
            o2_lo_wm = 0;
            h2_hi_wm = 0;
            h2_lo_wm = 0;
            material_threshold_multiplier = 1f;
        }

        // update defaults based on auto configured values
        void autoConfigure()
        {
            resetConfig();
            if (isBaseMode())
            {
                sort_storage = true;
            }
            else if (isDrillMode())
            {
                push_ore_to_base = true;
                if (can_refine)
                {
                    push_ingots_to_base = true;
                }
            }
            else if (isGrinderMode())
            {
                push_components_to_base = true;
            }
        }

        void configureAutoUpdate()
        {
            if (trigger_mode)
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
            } else if (green_mode)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                int ticks = secondsToTicks(update_period);

                // can't be less than 100
                ticks = Math.Max(ticks, 100);

                // round to nearest 100
                ticks = (int) Math.Round(ticks / 100.0) * 100;

                update_period = ticksToSeconds(ticks);
                update_counter_max = ticks / 100;
            } else
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update10;
                int ticks = secondsToTicks(update_period);
                
                // can't be less than 10
                ticks = Math.Max(ticks, 10);

                // round to nearest 10
                ticks = (int)Math.Round(ticks / 10.0) * 10;

                update_period = ticksToSeconds(ticks);
                update_counter_max = ticks / 10;
            }
        }

        void configureWatermarks()
        {
            if (isShipMode())
            {
                if (!has_reactors)
                {
                    auto_refuel_ship = false;
                }
                else
                {
                    auto_refuel_ship = true;
                }
            }
            if (pwr_lo_wm == 0)
            {
                if (isBaseMode())
                {
                    pwr_lo_wm = 60;
                }
                else
                {
                    pwr_lo_wm = 10;
                }
            }
            if (pwr_hi_wm == 0)
            {
                if (isBaseMode())
                {
                    pwr_hi_wm = 480;
                }
                else
                {
                    pwr_hi_wm = 30;
                }
            }
            if (o2_lo_wm == 0 && o2_hi_wm == 0 && has_oxygen_tanks)
            {
                if (isBaseMode())
                {
                    o2_lo_wm = 10;
                }
                else
                {
                    o2_lo_wm = 15;
                }
            }
            if (o2_hi_wm == 0 && has_oxygen_tanks)
            {
                if (isBaseMode())
                {
                    o2_hi_wm = 30;
                }
                else
                {
                    o2_hi_wm = 60;
                }
            }
            if (h2_lo_wm == 0 && h2_hi_wm == 0 && has_hydrogen_tanks)
            {
                if (isBaseMode())
                {
                    h2_lo_wm = 0;
                }
                else
                {
                    h2_lo_wm = 15;
                }
            }
            if (h2_hi_wm == 0 && has_hydrogen_tanks)
            {
                if (isBaseMode())
                {
                    h2_hi_wm = 30;
                }
                else
                {
                    h2_hi_wm = 70;
                }
            }
        }

        // select operation mode
        void selectOperationMode()
        {
            // if we found some thrusters or wheels, assume we're a ship
            if (local_grid_data.thrusters || local_grid_data.wheels)
            {
                // this is likely a drill ship
                if (local_grid_data.drills && !local_grid_data.welders && !local_grid_data.grinders)
                {
                    setMode(OP_MODE_DRILL);
                }
                // this is likely a welder ship
                else if (local_grid_data.welders && !local_grid_data.drills && !local_grid_data.grinders)
                {
                    setMode(OP_MODE_WELDER);
                }
                // this is likely a grinder ship
                else if (local_grid_data.grinders && !local_grid_data.drills && !local_grid_data.welders)
                {
                    setMode(OP_MODE_GRINDER);
                }
                // we don't know what the hell this is, so don't adjust the defaults
                else
                {
                    setMode(OP_MODE_SHIP);
                }
            }
            else
            {
                setMode(OP_MODE_BASE);
            }
        }

        bool clStrCompare(string str1, string str2)
        {
            return String.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
        }

        string getWatermarkStr(float low, float high)
        {
            return String.Format("{0:0} {1:0}", low, high);
        }

        bool parseWatermarkStr(string val, out float low, out float high)
        {
            low = 0;
            high = 0;

            // trim all multiple spaces
            var opt = System.Text.RegularExpressions.RegexOptions.None;
            var regex = new System.Text.RegularExpressions.Regex("\\s{2,}", opt);
            val = regex.Replace(val, " ");

            string[] strs = val.Split(' ');
            if (strs.Length != 2)
            {
                return false;
            }
            if (!float.TryParse(strs[0], out low))
            {
                return false;
            }
            if (!float.TryParse(strs[1], out high))
            {
                return false;
            }
            return true;
        }

        StringBuilder generateConfiguration()
        {
            var sb = new StringBuilder();

            if (isBaseMode())
            {
                config_options[CS_OP_MODE] = "base";
            }
            else if (isGenericShipMode())
            {
                config_options[CS_OP_MODE] = "ship";
            }
            else if (isDrillMode())
            {
                config_options[CS_OP_MODE] = "drill";
            }
            else if (isWelderMode())
            {
                config_options[CS_OP_MODE] = "welder";
            }
            else if (isGrinderMode())
            {
                config_options[CS_OP_MODE] = "grinder";
            }
            else if (isTugMode())
            {
                config_options[CS_OP_MODE] = "tug";
            }
            config_options[CS_HUD_NOTIFICATIONS] = hud_notifications.ToString();
            config_options[CS_POWER_WATERMARKS] = getWatermarkStr(pwr_lo_wm, pwr_hi_wm);
            if (isShipMode())
            {
                config_options[CS_PUSH_ORE] = push_ore_to_base.ToString();
                config_options[CS_PUSH_INGOTS] = push_ingots_to_base.ToString();
                config_options[CS_PUSH_COMPONENTS] = push_components_to_base.ToString();
                config_options[CS_PULL_ORE] = pull_ore_from_base.ToString();
                config_options[CS_PULL_INGOTS] = pull_ingots_from_base.ToString();
                config_options[CS_PULL_COMPONENTS] = pull_components_from_base.ToString();
                config_options[CS_REFUEL_OXYGEN] = refuel_oxygen.ToString();
                config_options[CS_REFUEL_HYDROGEN] = refuel_hydrogen.ToString();
            }
            config_options[CS_SORT_STORAGE] = sort_storage.ToString();
            if (throw_out_gravel)
            {
                if (material_thresholds[STONE] == 0)
                {
                    config_options[CS_KEEP_GRAVEL] = "none";
                }
                else
                {
                    config_options[CS_KEEP_GRAVEL] = Math.Floor((material_thresholds[STONE] * 5) / 1000).ToString();
                }
            }
            else
            {
                config_options[CS_KEEP_GRAVEL] = "all";
            }
            config_options[CS_OXYGEN_WATERMARKS] = String.Format("{0}", o2_hi_wm >= 0 ? getWatermarkStr(o2_lo_wm, o2_hi_wm) : "none");
            config_options[CS_HYDROGEN_WATERMARKS] = String.Format("{0}", h2_hi_wm >= 0 ? getWatermarkStr(h2_lo_wm, h2_hi_wm) : "none");
            config_options[CS_GREEN_MODE] = green_mode.ToString();
            config_options[CS_UPDATE_PERIOD] = trigger_mode ? "trigger" : String.Format("{0:0.0}", update_period);
            config_options[CS_MATERIAL_THRESHOLD_MULT] = material_threshold_multiplier.ToString();

            // currently selected operation mode
            sb.AppendLine("# Operation mode.");
            sb.AppendLine("# Can be auto, base, ship, tug, drill, welder or grinder.");
            var key = CS_OP_MODE;
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            sb.AppendLine("# Update period.");
            sb.AppendLine("# Can be \"trigger\", or a positive number (0.1 or higher).");
            sb.AppendLine("# \"Trigger\" means the script won't run itself at all.");
            sb.AppendLine("# Number indicates how often the script will run itself.");
            sb.AppendLine("# For example, period of 0.5 will run script twice per second.");
            key = CS_UPDATE_PERIOD;
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            sb.AppendLine("# Green mode (do less work per iteration).");
            sb.AppendLine("# This will make BARABAS slower to react, but less sim-speed hungry.");
            sb.AppendLine("# Can be True or False.");
            key = CS_GREEN_MODE;
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_HUD_NOTIFICATIONS;
            sb.AppendLine("# HUD notifications for blocks and antennas.");
            sb.AppendLine("# Can be True or False.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_POWER_WATERMARKS;
            sb.AppendLine("# Amount of power on batteries/reactors, in minutes.");
            sb.AppendLine("# Can be \"auto\", or two positive numbers.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refueling.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_OXYGEN_WATERMARKS;
            sb.AppendLine("# Percentage of oxygen to keep.");
            sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 0 and 100.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refining ice.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_HYDROGEN_WATERMARKS;
            sb.AppendLine("# Percentage of hydrogen to keep.");
            sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 0 and 100.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refining ice.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            if (isShipMode())
            {
                key = CS_REFUEL_OXYGEN;
                sb.AppendLine("# Automatically refuel oxygen on connection.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_REFUEL_HYDROGEN;
                sb.AppendLine("# Automatically refuel hydrogen on connection.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
            }
            key = CS_KEEP_GRAVEL;
            sb.AppendLine("# How much gravel to keep, in tons.");
            sb.AppendLine("# Can be a positive number, \"none\", \"all\" or \"auto\".");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_SORT_STORAGE;
            sb.AppendLine("# Automatically sort items in storage containers.");
            sb.AppendLine("# Can be True or False.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CS_MATERIAL_THRESHOLD_MULT;
            sb.AppendLine("# Multiplier for material shortage thresholds.");
            sb.AppendLine("# Can be used to raise or lower shortage alarm thresholds.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();

            // these values only apply to ships
            if (isShipMode())
            {
                key = CS_PUSH_ORE;
                sb.AppendLine("# Push ore to base storage.");
                sb.AppendLine("# In tug mode, also pull ore from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_PUSH_INGOTS;
                sb.AppendLine("# Push ingots to base storage.");
                sb.AppendLine("# In tug mode, also pull ingots from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_PUSH_COMPONENTS;
                sb.AppendLine("# Push components to base storage.");
                sb.AppendLine("# In tug mode, also pull components from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_PULL_ORE;
                sb.AppendLine("# Pull ore from base storage.");
                sb.AppendLine("# In tug mode, also push ore to ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_PULL_INGOTS;
                sb.AppendLine("# Pull ingots from base storage.");
                sb.AppendLine("# In tug mode, also push ingots to ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CS_PULL_COMPONENTS;
                sb.AppendLine("# Pull components from base storage.");
                sb.AppendLine("# In tug mode, also push components to ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
            }

            return sb;
        }

        void rebuildConfiguration()
        {
            Me.CustomData = generateConfiguration().ToString();
        }

        void parseLine(string line)
        {
            string[] strs = line.Split('=');
            if (strs.Length != 2)
            {
                throw new BarabasException("Invalid number of tokens: " + line, this);
            }
            var str = strs[0].ToLower().Trim();
            // backwards compatibility
            if (str == CS_KEEP_STONE)
                str = CS_KEEP_GRAVEL;
            if (!config_options.ContainsKey(str))
            {
                throw new BarabasException("Invalid config option: " + str, this);
            }
            var strval = strs[1].ToLower().Trim();
            // remove non-printable characters
            strval = System.Text.RegularExpressions.Regex.Replace(strval, @"[^\u0020-\u007E]", string.Empty);
            // now, try to parse it
            bool fail = false;
            bool bval, bparse, fparse;
            float fval;
            bparse = Boolean.TryParse(strval, out bval);
            fparse = float.TryParse(strval, out fval);

            // op mode
            if (clStrCompare(str, CS_OP_MODE))
            {
                if (strval == "base")
                {
                    if (!isBaseMode())
                    {
                        setMode(OP_MODE_BASE);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "ship")
                {
                    if (!isGenericShipMode())
                    {
                        setMode(OP_MODE_SHIP);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "drill")
                {
                    if (!isDrillMode())
                    {
                        setMode(OP_MODE_DRILL);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "welder")
                {
                    if (!isWelderMode())
                    {
                        setMode(OP_MODE_WELDER);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "grinder")
                {
                    if (!isGrinderMode())
                    {
                        setMode(OP_MODE_GRINDER);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "tug")
                {
                    if (!isTugMode())
                    {
                        setMode(OP_MODE_TUG);
                        crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    }
                }
                else if (strval == "auto")
                {
                    setMode(OP_MODE_AUTO);
                    crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                }
                else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CS_POWER_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    pwr_lo_wm = 0;
                    pwr_hi_wm = 0;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low > 0)
                    {
                        pwr_lo_wm = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high > 0 && low <= high)
                    {
                        pwr_hi_wm = high;
                    }
                    else
                    {
                        fail = true;
                    }
                }
                else
                {
                    fail = true;
                }
            }
            // backwards compatibility with old configs - we no longer throw out stone ore
            else if (clStrCompare(str, CS_KEEP_GRAVEL) || clStrCompare(str, CS_KEEP_STONE))
            {
                if (fparse && fval > 0)
                {
                    throw_out_gravel = true;
                    material_thresholds[STONE] = (float)Math.Floor((fval * 1000) / 5);
                }
                else if (strval == "all")
                {
                    throw_out_gravel = false;
                }
                else if (strval == "none")
                {
                    throw_out_gravel = true;
                    material_thresholds[STONE] = 0;
                }
                else if (strval == "auto")
                {
                    throw_out_gravel = true;
                    material_thresholds[STONE] = 5000;
                }
                else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CS_OXYGEN_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    o2_lo_wm = 0;
                    o2_hi_wm = 0;
                }
                else if (strval == "none")
                {
                    o2_lo_wm = -1;
                    o2_hi_wm = -1;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low >= 0 && low <= 100)
                    {
                        o2_lo_wm = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high >= 0 && high <= 100 && low <= high)
                    {
                        o2_hi_wm = high;
                    }
                    else
                    {
                        fail = true;
                    }
                }
                else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CS_HYDROGEN_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    h2_lo_wm = 0;
                    h2_hi_wm = 0;
                }
                else if (strval == "none")
                {
                    h2_lo_wm = -1;
                    h2_hi_wm = -1;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low >= 0 && low <= 100)
                    {
                        h2_lo_wm = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high >= 0 && high <= 100 && low <= high)
                    {
                        h2_hi_wm = high;
                    }
                    else
                    {
                        fail = true;
                    }
                }
                else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CS_UPDATE_PERIOD))
            {
                if (strval == "trigger")
                {
                    trigger_mode = true;
                } else if (fparse && fval >= 0.1f)
                {
                    trigger_mode = false;
                    update_period = fval;
                } else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CS_MATERIAL_THRESHOLD_MULT))
            {
                if (strval == "auto")
                {
                    material_threshold_multiplier = 1;
                } else if (fparse && fval >= 0.1f)
                {
                    material_threshold_multiplier = fval;
                } else
                {
                    fail = true;
                }
            }
            // bools
            else if (bparse)
            {
                if (isShipMode())
                {
                    if (clStrCompare(str, CS_PUSH_ORE))
                    {
                        push_ore_to_base = bval;
                    }
                    else if (clStrCompare(str, CS_PUSH_INGOTS))
                    {
                        push_ingots_to_base = bval;
                    }
                    else if (clStrCompare(str, CS_PUSH_COMPONENTS))
                    {
                        push_components_to_base = bval;
                    }
                    else if (clStrCompare(str, CS_PULL_ORE))
                    {
                        pull_ore_from_base = bval;
                    }
                    else if (clStrCompare(str, CS_PULL_INGOTS))
                    {
                        pull_ingots_from_base = bval;
                    }
                    else if (clStrCompare(str, CS_PULL_COMPONENTS))
                    {
                        pull_components_from_base = bval;
                    }
                    else if (clStrCompare(str, CS_REFUEL_OXYGEN))
                    {
                        refuel_oxygen = bval;
                    }
                    else if (clStrCompare(str, CS_REFUEL_HYDROGEN))
                    {
                        refuel_hydrogen = bval;
                    }
                }
                if (clStrCompare(str, CS_SORT_STORAGE))
                {
                    sort_storage = bval;
                }
                else if (clStrCompare(str, CS_HUD_NOTIFICATIONS))
                {
                    hud_notifications = bval;
                }
                else if (clStrCompare(str, CS_GREEN_MODE))
                {
                    green_mode = bval;
                }
            }
            else
            {
                fail = true;
            }
            if (fail)
            {
                throw new BarabasException("Invalid config value: " + strval, this);
            }
        }

        // this will find a BARABAS Config block and read its configuration
        void parseConfiguration()
        {
            string text = Me.CustomData;

            // check if the text is empty
            if (text.Trim().Length != 0)
            {
                var lines = text.Split('\n');
                foreach (var l in lines)
                {
                    var line = l.Trim();

                    // skip comments and empty lines
                    if (line.StartsWith("#") || line.Length == 0)
                    {
                        continue;
                    }
                    parseLine(line);
                }
            }
        }
        #endregion

        #region ALERTS

        void displayAlerts()
        {
            removeAntennaAlert(ALERT_LOW_POWER);
            removeAntennaAlert(ALERT_LOW_STORAGE);
            removeAntennaAlert(ALERT_VERY_LOW_STORAGE);
            removeAntennaAlert(ALERT_MATERIAL_SHORTAGE);
            displayAntennaAlerts();

            var sb = new StringBuilder();

            Alert first_alert = null;
            // now, find enabled alerts
            bool first = true;
            for (int i = 0; i < text_alerts.Count; i++)
            {
                if (text_alerts[i].enabled)
                {
                    if (i == (int)AlertLevel.ALERT_BLUE)
                    {
                        addAntennaAlert(ALERT_LOW_POWER);
                    }
                    else if (i == (int)AlertLevel.ALERT_YELLOW)
                    {
                        addAntennaAlert(ALERT_LOW_STORAGE);
                    }
                    else if (i == (int)AlertLevel.ALERT_RED)
                    {
                        addAntennaAlert(ALERT_VERY_LOW_STORAGE);
                    }
                    else if (i == (int)AlertLevel.ALERT_WHITE)
                    {
                        addAntennaAlert(ALERT_MATERIAL_SHORTAGE);
                    }
                    var alert = text_alerts[i];
                    if (first_alert == null)
                    {
                        first_alert = alert;
                    }
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    first = false;
                    sb.Append(alert.text);
                }
            }

            status_report[STATUS_ALERT] = sb.ToString();
            if (first_alert == null)
            {
                hideAlertColor();
            }
            else
            {
                showAlertColor(first_alert.color);
            }
        }

        void addAlert(AlertLevel level)
        {
            var alert = text_alerts[(int)level];
            // this alert is already enabled
            if (alert.enabled)
            {
                return;
            }
            alert.enabled = true;

            displayAlerts();
        }

        void removeAlert(AlertLevel level)
        {
            // disable the alert
            var alert = text_alerts[(int)level];
            alert.enabled = false;

            displayAlerts();
        }

        string getBlockAlerts(int ids)
        {
            var sb = new StringBuilder();
            int idx = 0;
            bool first = true;
            while (ids != 0)
            {
                if ((ids & 0x1) != 0)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    first = false;
                    sb.Append(block_alerts[1 << idx]);
                }
                idx++;
                ids >>= 1;
            }
            return sb.ToString();
        }

        void updateBlockName(IMyTerminalBlock b)
        {
            if (!hud_notifications)
            {
                return;
            }
            var name = getBlockName(b);
            if (!blocks_to_alerts.ContainsKey(b))
            {
                setBlockName(b, name, "");
                return;
            }
            var cur = blocks_to_alerts[b];
            var alerts = getBlockAlerts(cur);
            setBlockName(b, name, alerts);
        }

        void addBlockAlert(IMyTerminalBlock b, int id)
        {
            if (!hud_notifications)
            {
                return;
            }
            if (blocks_to_alerts.ContainsKey(b))
            {
                blocks_to_alerts[b] |= id;
            }
            else
            {
                blocks_to_alerts.Add(b, id);
            }
            if (hud_notifications && (b as IMyRadioAntenna) == null)
            {
                b.ShowOnHUD = true;
            }
        }

        void removeBlockAlert(IMyTerminalBlock b, int id)
        {
            if (!hud_notifications)
            {
                return;
            }
            if (blocks_to_alerts.ContainsKey(b))
            {
                var cur = blocks_to_alerts[b];
                cur &= ~id;
                if (cur != 0)
                {
                    blocks_to_alerts[b] = cur;
                }
                else
                {
                    blocks_to_alerts.Remove(b);
                    if ((b as IMyRadioAntenna) == null)
                        b.ShowOnHUD = false;
                }
            }
        }

        string getBlockName(IMyTerminalBlock b)
        {
            var name = b.CustomName;
            var regex = new System.Text.RegularExpressions.Regex("\\[BARABAS");
            var match = regex.Match(name);
            if (!match.Success)
            {
                return name;
            }
            return name.Substring(0, match.Index - 1);
        }

        void setBlockName(IMyTerminalBlock antenna, string name, string alert)
        {
            if (alert == "")
            {
                antenna.CustomName = name;
            }
            else
            {
                antenna.CustomName = name + " [BARABAS: " + alert + "]";
            }
        }

        void displayAntennaAlerts()
        {
            if (!hud_notifications)
            {
                return;
            }
            var antennas = getAntennas();
            foreach (var antenna in antennas)
            {
                updateBlockName(antenna);
            }
        }

        void addAntennaAlert(int id)
        {
            if (!hud_notifications)
            {
                return;
            }
            var antennas = getAntennas();
            foreach (var antenna in antennas)
            {
                addBlockAlert(antenna, id);
            }
        }

        void removeAntennaAlert(int id)
        {
            if (!hud_notifications)
            {
                return;
            }
            var antennas = getAntennas();
            foreach (var antenna in antennas)
            {
                removeBlockAlert(antenna, id);
            }
        }

        void showAlertColor(Color c)
        {
            var lights = getLights();
            foreach (IMyLightingBlock light in lights)
            {
                if (light.Color.Equals(c) && light.Enabled)
                {
                    continue;
                }
                light.Color = c;
                // make sure we switch the color of the texture as well
                light.Enabled = false;
                light.Enabled = true;
            }
        }

        void hideAlertColor()
        {
            var lights = getLights();
            foreach (IMyLightingBlock light in lights)
            {
                if (!light.Enabled)
                {
                    continue;
                }
                light.Enabled = false;
            }
        }

        void displayStatusReport()
        {
            var ps = getTextPanels();

            if (ps.Count == 0)
            {
                return;
            }

            removeAntennaAlert(ALERT_CRISIS_LOCKUP);
            removeAntennaAlert(ALERT_CRISIS_STANDBY);
            removeAntennaAlert(ALERT_CRISIS_THROW_OUT);
            removeAntennaAlert(ALERT_CRISIS_CRITICAL_POWER);

            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE && tried_throwing)
            {
                status_report[STATUS_CRISIS_MODE] = "Standby";
                addAntennaAlert(ALERT_CRISIS_STANDBY);
            }
            else if (crisis_mode == CrisisMode.CRISIS_MODE_NONE)
            {
                status_report[STATUS_CRISIS_MODE] = "";
            }
            else if (crisis_mode == CrisisMode.CRISIS_MODE_THROW_ORE)
            {
                status_report[STATUS_CRISIS_MODE] = "Ore throwout";
                addAntennaAlert(ALERT_CRISIS_THROW_OUT);
            }
            else if (crisis_mode == CrisisMode.CRISIS_MODE_LOCKUP)
            {
                status_report[STATUS_CRISIS_MODE] = "Lockup";
                addAntennaAlert(ALERT_CRISIS_LOCKUP);
            } else if (crisis_mode == CrisisMode.CRISIS_MODE_NO_POWER)
            {
                status_report[STATUS_CRISIS_MODE] = "Power level critical";
                addAntennaAlert(ALERT_CRISIS_CRITICAL_POWER);
            }

            displayAntennaAlerts();

            // construct panel text
            var sb = new StringBuilder();
            foreach (var pair in status_report)
            {
                if (pair.Value == "")
                {
                    continue;
                }
                sb.AppendLine(String.Format("{0}: {1}", pair.Key, pair.Value));
            }
            foreach (IMyTextPanel panel in ps)
            {
                panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                panel.WriteText(sb);
                panel.WritePublicTitle("BARABAS Notify Report");
            }
        }
        #endregion

        #region STATES
        /*
         * States
         */

        void s_refreshGrids()
        {
            getLocalGrids(true);
        }

        void s_refreshBlocks()
        {
            getBlocks(true);
        }

        void s_refreshProduction()
        {
            has_refineries = getRefineries(true).Count > 0;
            can_refine = has_refineries || (getOxygenGenerators(true).Count > 0);
            can_refine_ice = has_refineries || (getOxygenGenerators().Count > 0);
            can_use_ingots = getAssemblers(true).Count > 0;
            getStorage(true);
            turnOffConveyors();
        }

        void s_refreshPower()
        {
            has_reactors = getReactors(true).Count > 0;
            getBatteries(true);
            getPowerProducers(true);
            getMaxPowerOutput(true);
            getCurPowerDraw(true);
            getMaxPowerDraw(true);
            getTransientPower(true);
            getStoredPower(true);
        }

        void s_refreshOxyHydro()
        {
            has_air_vents = getAirVents(true).Count > 0;
            has_oxygen_tanks = getOxygenTanks(true).Count > 0;
            has_hydrogen_tanks = getHydrogenTanks(true).Count > 0;
            can_use_oxygen = has_oxygen_tanks && has_air_vents;
            getOxygenCapacity(true);
            getHydrogenCapacity(true);
            getStoredOxygen(true);
            getStoredHydrogen(true);
        }

        void s_refreshTools()
        {
            has_drills = getDrills(true).Count > 0;
            has_grinders = getGrinders(true).Count > 0;
            has_welders = getWelders(true).Count > 0;
        }

        void s_refreshMisc()
        {
            has_connectors = getConnectors(true).Count > 0;
            has_status_panels = getTextPanels(true).Count > 0;
            spreadLoad(getTrashConnectors(true));
            getTrashSensors(true);
            getLights(true);
            getAntennas(true);
            startThrowing();
        }

        void s_refreshConfig()
        {
            // configure BARABAS
            parseConfiguration();
            if (isAutoMode())
            {
                selectOperationMode();
                autoConfigure();
            }
            if (isShipMode())
            {
                Me.CustomName = "BARABAS Ship CPU";
            }
            else
            {
                Me.CustomName = "BARABAS Base CPU";
            }
            configureAutoUpdate();
            configureWatermarks();
            rebuildConfiguration();

            if (pull_ingots_from_base && push_ingots_to_base)
            {
                throw new BarabasException("Invalid configuration - " +
                    "pull_ingots_from_base and push_ingots_to_base both set to \"true\"", this);
            }
            if (pull_ore_from_base && push_ore_to_base)
            {
                throw new BarabasException("Invalid configuration - " +
                    "pull_ore_from_base and push_ore_to_base both set to \"true\"", this);
            }
            if (pull_components_from_base && push_components_to_base)
            {
                throw new BarabasException("Invalid configuration - " +
                    "pull_components_from_base and push_components_to_base both set to \"true\"", this);
            }
        }

        void s_refreshRemote()
        {
            // local refresh finished, now see if we have any remote grids
            findRemoteGrids();
            // signal that we have something connected to us
            if (connected)
            {
                getRemoteStorage(true);
                getRemoteShipStorage(true);
                getRemoteOxyHydroLevels();
                addAlert(AlertLevel.ALERT_GREEN);
            }
            else
            {
                removeAlert(AlertLevel.ALERT_GREEN);
            }
        }

        void s_power()
        {
            bool above_high_watermark = powerAboveHighWatermark();
            var max_pwr_output = getMaxPowerOutput();

            // if we have enough uranium ingots, business as usual
            if (!above_high_watermark)
            {
                // check if we're below low watermark
                bool above_low_watermark = powerAboveLowWatermark();

                if (has_reactors && has_refineries && ore_status[U] > 0)
                {
                    prioritize_uranium = true;
                }

                if (!above_low_watermark && max_pwr_output != 0)
                {
                    addAlert(AlertLevel.ALERT_BLUE);
                }
                else
                {
                    removeAlert(AlertLevel.ALERT_BLUE);
                }
            }
            else
            {
                removeAlert(AlertLevel.ALERT_BLUE);
                prioritize_uranium = false;
            }
            status_report[STATUS_POWER_STATS] = "No power sources";

            if (max_pwr_output != 0)
            {
                // figure out how much time we have on batteries and reactors
                float stored_power = getStoredPower();
                // transient power will offset some of the power draw
                var max_pwr_draw = getMaxPowerDraw() - getTransientPower();
                var cur_pwr_draw = getCurPowerDraw() - getTransientPower();
                // prevent division by zero or negatives
                max_pwr_draw = (float)Math.Max(max_pwr_draw, 0.001F);
                cur_pwr_draw = (float)Math.Max(cur_pwr_draw, 0.001F);

                // cap the max
                max_pwr_draw = (float)Math.Min(max_power_draw, max_pwr_output);

                // current power draw may be zero or negative in certain circumstances
                cur_pwr_draw = (float)Math.Max(cur_pwr_draw, 0.001F);

                // we're averaging over current and previous value
                var adjusted_pwr_draw = (cur_pwr_draw + prev_pwr_draw) / 2;
                prev_pwr_draw = cur_pwr_draw;

                float time, orig_time;
                string time_str;
                if (isShipMode() && connected_to_base)
                {
                    time = (float)Math.Round(stored_power / max_pwr_draw, 0);
                }
                else
                {
                    time = (float)Math.Round(stored_power / adjusted_pwr_draw, 0);
                }
                // calculate minutes remaining
                orig_time = time;
                if (time > 300)
                {
                    time = (float)Math.Floor(time / 60);
                    if (time > 48)
                    {
                        time = (float)Math.Floor(time / 24);
                        if (time > 30)
                        {
                            time_str = "lots";
                        }
                        else
                        {
                            time_str = time.ToString() + " d";
                        }
                    }
                    else
                    {
                        time_str = time.ToString() + " h";
                    }
                }
                else
                {
                    time_str = time.ToString() + " m";
                }
                string max_str = String.Format("{0:0.0}%", max_pwr_draw / max_pwr_output * 100);
                string cur_str = String.Format("{0:0.0}%", adjusted_pwr_draw / max_pwr_output * 100);
                status_report[STATUS_POWER_STATS] = String.Format("{0}/{1}/{2}", max_str, cur_str, time_str);

                if (!isShipMode() && stored_power_thresh == 0 && orig_time < 5)
                {
                    // we're in a crisis - we have no power left, so shut everything down
                    crisis_mode = CrisisMode.CRISIS_MODE_NO_POWER;
                    addAlert(AlertLevel.ALERT_BLUE);
                    // remember the amount of power we had when we had to shut everything down,
                    // and wait until we get 3 more power before we exit crisis mode
                    stored_power_thresh = stored_power * 3;
                }
                else if (stored_power < stored_power_thresh)
                {
                    // we still don't have enough stored power to exit crisis mode
                    crisis_mode = CrisisMode.CRISIS_MODE_NO_POWER;
                    addAlert(AlertLevel.ALERT_BLUE);
                }
                else if (crisis_mode == CrisisMode.CRISIS_MODE_NO_POWER || connected_to_base)
                {
                    // if we're connected to base, assume crisis is averted
                    crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                    stored_power_thresh = 0;
                }
            }
            foreach (IMyRefinery r in getRefineries())
            {
                r.Enabled = true;
            }
            foreach (IMyAssembler r in getAssemblers())
            {
                r.Enabled = true;
            }

            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE)
            {
                if (refillReactors())
                {
                    pushSpareUraniumToStorage();
                }
            }
            // if we're in a crisis, push all available uranium ingots to reactors.
            else if (crisis_mode != CrisisMode.CRISIS_MODE_NO_POWER)
            {
                refillReactors(true);
            }
            else if (crisis_mode == CrisisMode.CRISIS_MODE_NO_POWER)
            {
                addAlert(AlertLevel.ALERT_BLUE);

                // if there's no power, shut everything down
                foreach (IMyRefinery r in getRefineries())
                {
                    r.Enabled = false;
                }
                foreach (IMyAssembler r in getAssemblers())
                {
                    r.Enabled = false;
                }
                foreach (IMyGasGenerator g in getOxygenGenerators())
                {
                    g.Enabled = false;
                }
            }
        }

        void s_refineries()
        {
            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE && can_refine)
            {
                // if we're a ship and we're connected, push ore to storage
                if (isShipMode() && push_ore_to_base && connected_to_base)
                {
                    pushOreToStorage();
                }
                else
                {
                    refineOre();
                }
            }
        }

        void s_processIce()
        {
            toggleOxygenGenerators(refine_ice);
            if (refine_ice)
            {
                return;
            }
            pushIceToStorage();
        }

        void s_refuelIce()
        {
            var o = getOxygenTanks();
            var h = getHydrogenTanks();
            var o_s = false;
            var h_s = false;

            // if we're not a ship or we're not connected to anything, bail out
            if (!isShipMode() || !connected)
            {
                toggleStockpile(o, o_s);
                toggleStockpile(h, h_s);
                return;
            }
            if (refuel_oxygen && can_refuel_oxygen && !oxygenAboveHighWatermark())
            {
                o_s = true;
            }
            if (refuel_hydrogen && can_refuel_hydrogen && !hydrogenAboveHighWatermark())
            {
                h_s = true;
            }
            toggleStockpile(o, o_s);
            toggleStockpile(h, h_s);
        }

        void s_materialsPriority()
        {
            // check if any ore needs to be prioritized
            if (can_use_ingots || prioritize_uranium)
            {
                reprioritizeOre();
            }
        }

        void s_materialsRebalance()
        {
            if (can_refine)
            {
                rebalanceRefineries();
            }
        }

        void s_materialsCrisis()
        {
            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE && throw_out_gravel)
            {
                // check if we want to throw out extra gravel
                if (storage_ingot_status[STONE] > 0)
                {
                    var excessGravel = storage_ingot_status[STONE] - material_thresholds[STONE] * 5 * material_threshold_multiplier;
                    if (excessGravel > 0)
                    {
                        throwOutIngots(STONE, excessGravel);
                    }
                    else
                    {
                        storeTrash();
                    }
                }
            }
            else if (crisis_mode == CrisisMode.CRISIS_MODE_THROW_ORE)
            {
                // if we can't even throw out ore, well, all bets are off
                string o = getBiggestOre();
                if ((o != null && !throwOutOre(o, 0, true)) || o == null)
                {
                    storeTrash(true);
                    crisis_mode = CrisisMode.CRISIS_MODE_LOCKUP;
                }
            }
        }

        void s_toolsDrills()
        {
            if (has_drills)
            {
                var d = getDrills();
                emptyBlocks(d);
                spreadLoad(d);
            }
        }

        void s_toolsGrinders()
        {
            if (has_grinders)
            {
                var g = getGrinders();
                emptyBlocks(g);
                spreadLoad(g);
            }
        }

        void s_toolsWelders()
        {
            if (has_welders && isWelderMode())
            {
                fillWelders();
            }
        }

        void s_declogAssemblers()
        {
            if (can_use_ingots)
            {
                declogAssemblers();
            }
        }

        void s_declogRefineries()
        {
            if (can_refine)
            {
                declogRefineries();
            }
        }

        void s_localStorage()
        {
            if (sort_storage)
            {
                sortLocalStorage();
            }
        }

        void s_remoteStorage()
        {
            if (isShipMode() && connected_to_base)
            {
                pushAllToRemoteStorage();
                pullFromRemoteStorage();
            }
            // tug is a special case as it can push to and pull from ships, but only
            // when connected to a ship and not to a base
            else if (isTugMode() && connected_to_ship && !connected_to_base)
            {
                pullFromRemoteShipStorage();
                pushToRemoteShipStorage();
            }
        }

        string roundStr(float val)
        {
            if (val >= 10000)
            {
                return String.Format("{0}k", Math.Floor(val / 1000));
            }
            else if (val >= 1000)
            {
                return String.Format("{0:0.#}k", val / 1000);
            }
            else if (val >= 100)
            {
                return String.Format("{0}", Math.Floor(val));
            }
            else
            {
                return String.Format("{0:0.#}", val);
            }
        }

        void s_updateMaterialStats()
        {
            float uranium_in_reactors = 0;
            // clear stats
            foreach (var ore in ore_types)
            {
                ore_status[ore] = 0;
                storage_ore_status[ore] = 0;
                ingot_status[ore] = 0;
                storage_ingot_status[ore] = 0;
            }
            assembler_ingots = new HashSet<string>();

            var blocks = getBlocks();
            foreach (var b in blocks)
            {
                for (int j = 0; j < b.InventoryCount; j++)
                {
                    var items = new List<MyInventoryItem>();
                    var inv = b.GetInventory(j);
                    inv.GetItems(items);
                    foreach (var i in items)
                    {
                        bool isStorage = b is IMyCargoContainer;
                        bool isReactor = b is IMyReactor;
                        if (isOre(i))
                        {
                            string name = i.Type.SubtypeId;
                            if (i.Type.SubtypeId == SCRAP)
                            {
                                name = FE;
                            }
                            ore_status[name] += (float)i.Amount;
                            if (isStorage)
                            {
                                storage_ore_status[name] += (float)i.Amount;
                            }
                        }
                        else if (isIngot(i))
                        {
                            string name = i.Type.SubtypeId;
                            var amount = (float)i.Amount;
                            ingot_status[name] += amount;
                            if (isStorage)
                            {
                                storage_ingot_status[name] += amount;
                            }
                            if (isReactor)
                            {
                                uranium_in_reactors += amount;
                            }
                        }
                    }
                }
                if (b is IMyAssembler)
                {
                    // add all ore that's needed in assembler to the list
                    var a = b as IMyAssembler;

                    var pq = new List<MyProductionItem>();
                    a.GetQueue(pq);

                    // count first three items, no need to count everything
                    int i = 0;
                    foreach (var pi in pq)
                    {
                        var ingot_list = new List<string>();
                        if (!production_item_ingots.TryGetValue(pi.BlueprintId.SubtypeName, out ingot_list))
                        {
                            // we couldn't find this item in our list of possible production items, so simply ignore it
                            continue;
                        }
                        foreach (var ingot in ingot_list)
                        {
                            assembler_ingots.Add(ingot);
                        }
                        if (++i == 3)
                            break;
                    }
                }
            }
            bool alert = false;
            var sb = new StringBuilder();
            foreach (var o in ore_types)
            {
                float total_ingots = 0;
                float total_ore = ore_status[o];
                float total = 0;
                bool has_stone = false;
                if (o != ICE)
                {
                    total_ingots = ingot_status[o];
                    // check if ore is produced by stone, but exclude stone itself to avoid counting twice
                    bool is_stone_ore = (o != STONE) && ore_to_ingot_ratios[STONE].ContainsKey(o);
                    if (o == U)
                    {
                        total_ingots -= uranium_in_reactors;
                    }
                    total = (total_ore * ore_to_ingot_ratios[o][o]) + total_ingots;
                    if (is_stone_ore)
                    {
                        total += (ore_status[STONE] * ore_to_ingot_ratios[STONE][o]);
                        has_stone = true;
                    }
                }
                else
                {
                    total = total_ore;
                }

                if (has_status_panels)
                {
                    if (isShipMode() && total == 0)
                    {
                        continue;
                    }
                    sb.Append("\n  ");
                    sb.Append(o);
                    sb.Append(": ");
                    sb.Append(roundStr(total_ore));

                    if (o != ICE)
                    {
                        sb.Append(" / ");
                        sb.Append(roundStr(total_ingots));
                        if (total_ore > 0 || has_stone)
                        {
                            sb.Append(String.Format(" ({0})", roundStr(total)));
                        }
                    } else if (total_ore > 0)
                    {
                        var liters = total_ore * ICE_TO_GAS_RATIO;
                        sb.AppendFormat(" / {0}L", getMagnitudeStr(liters));
                    }
                }

                if (o != ICE && can_use_ingots && total < (material_thresholds[o] * material_threshold_multiplier))
                {
                    alert = true;
                    if (has_status_panels)
                    {
                        sb.Append(" WARNING!");
                    }
                }
            }
            if (alert)
            {
                addAlert(AlertLevel.ALERT_WHITE);
            }
            else
            {
                removeAlert(AlertLevel.ALERT_WHITE);
            }
            alert = false;
            status_report[STATUS_MATERIAL] = sb.ToString();

            // display oxygen and hydrogen stats
            if (has_oxygen_tanks || has_hydrogen_tanks)
            {
                float o2_cur = getStoredOxygen(), oxy_total = getOxygenCapacity();
                float h2_cur = getStoredHydrogen(), hydro_total = getHydrogenCapacity();
                
                float cur_o2_level = has_oxygen_tanks ? (o2_cur / oxy_total) * 100 : 0;
                float cur_h2_level = has_hydrogen_tanks ? (h2_cur / hydro_total) * 100 : 0;

                string oxy_str = !has_oxygen_tanks ?
                    "N/A" :
                    String.Format("{0:0.0}% {1}L", cur_o2_level, getMagnitudeStr(o2_cur));
                string hydro_str = !has_hydrogen_tanks ?
                    "N/A" :
                    String.Format("{0:0.0}% {1}L", cur_h2_level, getMagnitudeStr(h2_cur));

                if (!oxygenAboveLowWatermark())
                {
                    alert = true;
                    addAntennaAlert(ALERT_LOW_OXYGEN);
                }
                else
                {
                    removeAntennaAlert(ALERT_LOW_OXYGEN);
                }
                if (!hydrogenAboveLowWatermark())
                {
                    alert = true;
                    addAntennaAlert(ALERT_LOW_HYDROGEN);
                }
                else
                {
                    removeAntennaAlert(ALERT_LOW_HYDROGEN);
                }
                if (alert)
                {
                    status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1} !!!",
                        oxy_str, hydro_str);
                    addAlert(AlertLevel.ALERT_CYAN);
                }
                else
                {
                    status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1}",
                        oxy_str, hydro_str);
                    removeAlert(AlertLevel.ALERT_CYAN);
                }
            }
            else
            {
                removeAntennaAlert(ALERT_LOW_OXYGEN);
                removeAntennaAlert(ALERT_LOW_HYDROGEN);
                status_report[STATUS_OXYHYDRO_LEVEL] = "";
                removeAlert(AlertLevel.ALERT_CYAN);
            }
        }
        #endregion

        #region STATE_MACHINE
        int[] state_cycle_counts;
        int cur_cycle_count = 0;

        bool canContinue()
        {
            var prev_state = current_state == 0 ? states.Length - 1 : current_state - 1;
            var next_state = (current_state + 1) % states.Length;
            var cur_i = Runtime.CurrentInstructionCount;

            // check if we ever executed the next state and therefore can estimate how
            // much cycle count it will likely take
            bool canEstimate = state_cycle_counts[next_state] != 0;

            // store how many cycles we've used during this state
            state_cycle_counts[current_state] = cur_i - cur_cycle_count;

            // how many cycles did the next state take when it was last executed?
            var last = state_cycle_counts[next_state];

            // estimate cycle count after executing the next state
            int projected = cur_i + last;

            // given our estimate, how are we doing with regards to IL count limits?
            float cycle_p = projected / Runtime.MaxInstructionCount;

            // if we never executed the next state, we leave 60% headroom for our next
            // state (for all we know it could be a big state), otherwise leave at 20%
            // because we already know how much it usually takes and it's unlikely to
            // suddenly become much bigger than what we've seen before
            var thresh = canEstimate ? 0.8F : 0.4F;

            // if we're in green mode, set our threshold to 10%
            thresh = green_mode ? 0.1F : thresh;

            // check if we are exceeding our stated thresholds (projected 80% cycle
            // count for known states, or 40% up to this point for unknown states)
            bool haveHeadroom = cycle_p <= thresh;

            // advance current state and store IL count values
            current_state = next_state;
            cur_cycle_count = cur_i;

            return haveHeadroom && next_state != 0;
        }

        // check if we are disabled or if we should disable other BARABAS instances
        bool canRun()
        {
            bool isDisabled = Me.CustomName.Contains("DISABLED");
            var pbs = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(pbs, localGridFilter);

            foreach (var b in pbs)
            {
                if (b == Me)
                {
                    continue;
                }
                if (!b.CustomName.Contains("BARABAS"))
                {
                    continue;
                }
                // if we aren't disabled, disable all rival BARABAS instances
                if (!isDisabled && !b.CustomName.Contains("DISABLED"))
                {
                    b.CustomName = b.CustomName + " [DISABLED]";
                }
                else if (isDisabled)
                {
                    return false;
                }
            }
            return true;
        }
        #endregion

        #region INIT
        public void Save()
        {
            var sb = new StringBuilder();
            sb.Append(tried_throwing.ToString());
            Storage = sb.ToString();
        }

        // constructor
        public Program()
        {
            green_mode = false;
            update_counter_max = 1;
            Runtime.UpdateFrequency = UpdateFrequency.Once; // run at least once

            // kick off state machine
            states = new Action[] {
                s_refreshGrids,
                s_refreshBlocks,
                s_refreshPower,
                s_refreshProduction,
                s_refreshOxyHydro,
                s_refreshTools,
                s_refreshMisc,
                s_refreshConfig,
                s_refreshRemote,
                s_updateMaterialStats,
                s_power,
                s_refineries,
                s_processIce,
                s_refuelIce,
                s_materialsPriority,
                s_materialsRebalance,
                s_materialsCrisis,
                s_toolsDrills,
                s_toolsGrinders,
                s_toolsWelders,
                s_declogAssemblers,
                s_declogRefineries,
                s_localStorage,
                s_remoteStorage
            };
            current_state = 0;
            state_cycle_counts = new int[states.Length];

            for (int i = 0; i < state_cycle_counts.Length; i++)
            {
                state_cycle_counts[i] = 0;
            }

            // determine grid size
            var bd = Me.BlockDefinition.SubtypeName;
            large_grid = bd.Contains("Large");

            Me.ShowOnHUD = false;

            if (Storage.Length > 0)
            {
                bool invalid = false;
                string[] strs = Storage.Split(':');

                string throw_str = null;
                
                switch (strs.Length)
                {
                    case 1:
                        throw_str = strs[0];
                        break;
                    default:
                        invalid = true;
                        break;
                }
                if (invalid || !Boolean.TryParse(throw_str, out tried_throwing))
                {
                    // invalid data in Storage, erase it
                    Storage = "";
                }
                Save();
            }

            // initialize readonly vars
            ingot_status = new Dictionary<string, float>(ore_status);
            storage_ore_status = new Dictionary<string, float>(ore_status);
            storage_ingot_status = new Dictionary<string, float>(ore_status);
        }

        public void Main(string arg, UpdateType ut)
        {
            if (!canRun())
            {
                return;
            }
            if (!trigger_mode)
            {
                update_counter = (update_counter + 1) % update_counter_max;
                if (update_counter != 0)
                {
                    return;
                }
            }
            int num_states = 0;

            // zero out IL counters
            cur_cycle_count = 0;

            // clear set of lists we have refreshed during this iteration
            null_list = new HashSet<List<IMyTerminalBlock>>();
            do
            {
                try
                {
                    states[current_state]();
                }
                catch (BarabasException e)
                {
                    // if we caught our own exception, pass it along
                    Echo(e.StackTrace);
                    throw;
                }
                catch (Exception e)
                {
                    Echo(e.StackTrace);
                    string msg = String.Format("State: {0} Error: {1}", current_state, e.Message);
                    throw new BarabasException(msg, this);
                }
                num_states++;
            } while (canContinue() && num_states < states.Length);

            if (trashSensorsActive())
            {
                storeTrash(true);
            }

            // check storage load at each iteration
            checkStorageLoad();

            // check for leaks
            if (can_use_oxygen)
            {
                checkOxygenLeaks();
            }

            if (refineries_clogged || assemblers_clogged)
            {
                addAlert(AlertLevel.ALERT_MAGENTA);
            }
            else
            {
                removeAlert(AlertLevel.ALERT_MAGENTA);
            }

            // display status updates
            if (has_status_panels)
            {
                displayStatusReport();
            }
            var sb = new StringBuilder();
            sb.AppendFormat("BARABAS version {0}\r\n", VERSION);
            sb.AppendFormat("States executed: {0}\r\n", num_states);
            if (trigger_mode)
                sb.AppendLine("Update period: triggered");
            else
                sb.AppendFormat("Update period: {0:0.0} seconds\r\n", update_period);
            if (green_mode)
                sb.AppendFormat("Green mode active\r\n");
            sb.AppendFormat("IL Count: {0}/{1} ({2:0.0}%)",
                Runtime.CurrentInstructionCount,
                Runtime.MaxInstructionCount,
                (float)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount * 100F);
            Echo(sb.ToString());
            
            // also write it out on the screen
            var surface = Me.GetSurface(0);
            surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            surface.WriteText(sb.ToString());
        }
        #endregion
        #region SEFOOTER
/*
 * this is removed by my Minifier.
 */
        #if DEBUG
    }
}
#endif
#endregion
