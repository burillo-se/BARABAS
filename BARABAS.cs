#region SEHEADER
#if DEBUG
using System;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Collections;
using Sandbox.ModAPI.Ingame;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;

namespace SpaceEngineers
{
    public sealed class Program : MyGridProgram
    {
#endif
        #endregion
        /*
         * BARABAS v1.56
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
         * - A timer to run this code (recommended timer value is one second)
         *
         * Optional requirements:
         * - Group of text/LCD panels/beacons/antennas/lights named "BARABAS Notify",
         *   used for notification and status reports.
         * - A group of sensors and connectors named "BARABAS Trash".
         *
         * Trash throw out:
         * - During normal operation, only excess stone will be thrown out as trash.
         * - During crisis mode, some ore may be thrown out as well.
         * - If you don't want stone to be thrown out (e.g. if you use concrete mod),
         *   create a config block and edit configuration accordingly.
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

        const string VERSION = "1.56";

        // configuration
        const int OP_MODE_AUTO = 0x0;
        const int OP_MODE_SHIP = 0x1;
        const int OP_MODE_DRILL = 0x2;
        const int OP_MODE_GRINDER = 0x4;
        const int OP_MODE_WELDER = 0x8;
        const int OP_MODE_TUG = 0x10;
        const int OP_MODE_BASE = 0x100;

        int op_mode = OP_MODE_AUTO;
        float power_high_watermark = 0;
        float power_low_watermark = 0;
        float oxygen_high_watermark = 0;
        float oxygen_low_watermark = 0;
        float hydrogen_high_watermark = 0;
        float hydrogen_low_watermark = 0;
        bool throw_out_stone = false;
        bool sort_storage = true;
        bool hud_notifications = true;
        float prev_pwr_draw = 0;
        bool refineries_clogged = false;
        bool arc_furnaces_clogged = false;
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

        // crisis mode levels
        enum CrisisMode
        {
            CRISIS_MODE_NONE = 0,
            CRISIS_MODE_THROW_ORE,
            CRISIS_MODE_LOCKUP
        };
        CrisisMode crisis_mode;
        bool timer_mode = false;
        int pause_idx = 0; // we execute every 60 ticks or so

        Action[] states = null;

        // config options
        const string CONFIGSTR_OP_MODE = "mode";
        const string CONFIGSTR_POWER_WATERMARKS = "power watermarks";
        const string CONFIGSTR_PUSH_ORE = "push ore to base";
        const string CONFIGSTR_PUSH_INGOTS = "push ingots to base";
        const string CONFIGSTR_PUSH_COMPONENTS = "push components to base";
        const string CONFIGSTR_PULL_ORE = "pull ore from base";
        const string CONFIGSTR_PULL_INGOTS = "pull ingots from base";
        const string CONFIGSTR_PULL_COMPONENTS = "pull components from base";
        const string CONFIGSTR_KEEP_STONE = "keep stone";
        const string CONFIGSTR_SORT_STORAGE = "sort storage";
        const string CONFIGSTR_HUD_NOTIFICATIONS = "HUD notifications";
        const string CONFIGSTR_OXYGEN_WATERMARKS = "oxygen watermarks";
        const string CONFIGSTR_HYDROGEN_WATERMARKS = "hydrogen watermarks";
        const string CONFIGSTR_REFUEL_OXYGEN = "refuel oxygen";
        const string CONFIGSTR_REFUEL_HYDROGEN = "refuel hydrogen";

        // ore_volume
        const float VOLUME_ORE = 0.37F;
        const float VOLUME_SCRAP = 0.254F;

        // ore names
        const string COBALT = "Cobalt";
        const string GOLD = "Gold";
        const string STONE = "Stone";
        const string ICE = "Ice";
        const string IRON = "Iron";
        const string MAGNESIUM = "Magnesium";
        const string NICKEL = "Nickel";
        const string PLATINUM = "Platinum";
        const string SILICON = "Silicon";
        const string SILVER = "Silver";
        const string URANIUM = "Uranium";
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
            { CONFIGSTR_OP_MODE, "" },
            { CONFIGSTR_HUD_NOTIFICATIONS, "" },
            { CONFIGSTR_POWER_WATERMARKS, "" },
            { CONFIGSTR_OXYGEN_WATERMARKS, "" },
            { CONFIGSTR_HYDROGEN_WATERMARKS, "" },
            { CONFIGSTR_REFUEL_OXYGEN, "" },
            { CONFIGSTR_REFUEL_HYDROGEN, "" },
            { CONFIGSTR_PUSH_ORE, "" },
            { CONFIGSTR_PUSH_INGOTS, "" },
            { CONFIGSTR_PUSH_COMPONENTS, "" },
            { CONFIGSTR_PULL_ORE, "" },
            { CONFIGSTR_PULL_INGOTS, "" },
            { CONFIGSTR_PULL_COMPONENTS, "" },
            { CONFIGSTR_KEEP_STONE, "" },
            { CONFIGSTR_SORT_STORAGE, "" }
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
            COBALT, GOLD, IRON, MAGNESIUM, NICKEL, PLATINUM, SILICON, SILVER, URANIUM, STONE, ICE
        };

        readonly List<string> arc_furnace_ores = new List<string> {
            COBALT, IRON, NICKEL
        };

        // ballpark values of "just enough" for each material
        readonly Dictionary<string, float> material_thresholds = new Dictionary<string, float> {
            { COBALT, 500 },
            { GOLD, 100 },
            { IRON, 5000 },
            { MAGNESIUM, 100 },
            { NICKEL, 1000 },
            { PLATINUM, 10 },
            { SILICON, 1000 },
            { SILVER, 1000 },
            { URANIUM, 10 },
            { STONE, 5000 },
        };

        readonly Dictionary<string, float> ore_to_ingot_ratios = new Dictionary<string, float> {
            { COBALT, 0.24F },
            { GOLD, 0.008F },
            { IRON, 0.56F },
            { MAGNESIUM, 0.0056F },
            { NICKEL, 0.32F },
            { PLATINUM, 0.004F },
            { SILICON, 0.56F },
            { SILVER, 0.08F },
            { URANIUM, 0.0056F },
            { STONE, 0.72F }
        };

        // statuses for ore and ingots
        readonly Dictionary<string, float> ore_status = new Dictionary<string, float> {
            { COBALT, 0 },
            { GOLD, 0 },
            { ICE, 0 },
            { IRON, 0 },
            { MAGNESIUM, 0 },
            { NICKEL, 0 },
            { PLATINUM, 0 },
            { SILICON, 0 },
            { SILVER, 0 },
            { URANIUM, 0 },
            { STONE, 0 },
        };
        readonly Dictionary<string, float> ingot_status;
        readonly Dictionary<string, float> storage_ore_status;
        readonly Dictionary<string, float> storage_ingot_status;

        /* local data storage, updated once every few cycles */
        List<IMyTerminalBlock> local_blocks = null;
        List<IMyTerminalBlock> local_reactors = null;
        List<IMyTerminalBlock> local_batteries = null;
        List<IMyTerminalBlock> local_refineries = null;
        List<IMyTerminalBlock> local_refineries_subset = null;
        List<IMyTerminalBlock> local_arc_furnaces = null;
        List<IMyTerminalBlock> local_arc_furnaces_subset = null;
        List<IMyTerminalBlock> local_all_refineries = null;
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
        List<IMyTerminalBlock> local_trash_connectors = null;
        List<IMyTerminalBlock> local_trash_sensors = null;
        List<IMyTerminalBlock> local_antennas = null;
        List<IMyTerminalBlock> remote_storage = null;
        List<IMyTerminalBlock> remote_ship_storage = null;
        IMyTextPanel config_block = null;
        List<IMyCubeGrid> local_grids = null;
        List<IMyCubeGrid> remote_base_grids = null;
        List<IMyCubeGrid> remote_ship_grids = null;
        Dictionary<IMyCubeGrid, GridData> remote_grid_data = null;
        GridData local_grid_data = null;

        // alert levels, in priority order
        enum AlertLevel
        {
            RED_ALERT = 0,
            YELLOW_ALERT,
            BLUE_ALERT,
            CYAN_ALERT,
            MAGENTA_ALERT,
            WHITE_ALERT,
            PINK_ALERT,
            BROWN_ALERT,
            GREEN_ALERT
        };

        public class Alert
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
        const int ALERT_DISCONNECTED = 0x2000;

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
            { ALERT_DISCONNECTED, "Block not connected" },
        };

        /* misc local data */
        bool power_above_threshold = false;
        float cur_power_draw;
        float max_power_draw;
        float max_battery_output;
        float max_reactor_output;
        float cur_reactor_output;
        float cur_oxygen_level;
        float cur_hydrogen_level;
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
        bool has_arc_furnaces;
        bool connected;
        bool connected_to_base;
        bool connected_to_ship;

        // thrust block definitions
        Dictionary<string, float> thrust_power = new Dictionary<string, float>() {
            { "MyObjectBuilder_Thrust/SmallBlockSmallThrust", 33.6F },
            { "MyObjectBuilder_Thrust/SmallBlockLargeThrust", 400 },
            { "MyObjectBuilder_Thrust/LargeBlockSmallThrust", 560 },
            { "MyObjectBuilder_Thrust/LargeBlockLargeThrust", 6720 },
            { "MyObjectBuilder_Thrust/SmallBlockSmallHydrogenThrust", 0 },
            { "MyObjectBuilder_Thrust/SmallBlockLargeHydrogenThrust", 0 },
            { "MyObjectBuilder_Thrust/LargeBlockSmallHydrogenThrust", 0 },
            { "MyObjectBuilder_Thrust/LargeBlockLargeHydrogenThrust", 0 },
            { "MyObjectBuilder_Thrust/SmallBlockSmallAtmosphericThrust", 700 },
            { "MyObjectBuilder_Thrust/SmallBlockLargeAtmosphericThrust", 2400 },
            { "MyObjectBuilder_Thrust/LargeBlockSmallAtmosphericThrust", 2360 },
            { "MyObjectBuilder_Thrust/LargeBlockLargeAtmosphericThrust", 16360 }
        };

        // power constants - in kWatts
        const float URANIUM_INGOT_POWER = 68760;

        public class ItemHelper
        {
            public IMyTerminalBlock Owner;
            public int InvIdx;
            public IMyInventoryItem Item;
            public int Index;
        }

        // data we store about a grid
        // technically, while we use this class to store data about grids, what we
        // really want is to have an instance of this class per grid collection, i.e.
        // all grids that are local to each other (connected by rotors or pistons).
        // this is why it's a struct, not a class - so that several grids can share the
        // same instance. it's a crude hack, but it works.
        public class GridData
        {
            public bool has_thrusters;
            public bool has_wheels;
            public bool has_welders;
            public bool has_grinders;
            public bool has_drills;
            public bool override_ship;
            public bool override_base;
        }

        // grid graph edge class, represents a connection point between two grids.
        public class Edge<T>
        {
            public T src { get; set; }
            public T dst { get; set; }
        }

        // comparer for graph edges - the way the comparison is done means the edges are
        // bidirectional - meaning, it doesn't matter which grid is source and which
        // grid is destination, they will be equal as far as comparison is concerned.
        public class EdgeComparer<T> : IEqualityComparer<Edge<T>>
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
        public class Graph<T>
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
                        p.WritePublicText(" BARABAS EXCEPTION:\n" + msg);
                        p.ShowTextureOnScreen();
                        p.ShowPublicTextOnScreen();
                    }
                }
                pr.Me.CustomName = "BARABAS Exception: " + msg;
                pr.Me.ShowOnHUD = true;
                if (pr.local_lights != null)
                {
                    pr.showAlertColor(Color.Red);
                }
            }
        }

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

        /**
         * Grid and block functions
         */
        // filter blocks by type and locality
        public void filterLocalGrid<T>(List<IMyTerminalBlock> blocks, bool ignore_exclude = false)
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
            Random rng = new Random();
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
                addAlert(AlertLevel.PINK_ALERT);
            }
            else
            {
                removeAlert(AlertLevel.PINK_ALERT);
            }
            return local_blocks;
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
                if (inv.GetItems().Count > 1)
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
                if ((local_batteries[i] as IMyBatteryBlock).OnlyRecharge)
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
                if (!r.IsQueueEmpty && !r.IsProducing)
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

        List<IMyTerminalBlock> getArcFurnaces(bool force_update = false)
        {
            if (local_arc_furnaces != null && !force_update)
            {
                // if we didn't refresh the list yet, get a random subset
                if (!null_list.Contains(local_arc_furnaces_subset))
                {
                    local_arc_furnaces_subset = randomSubset(local_arc_furnaces, 40);
                }
                return removeNulls(local_arc_furnaces_subset);
            }
            arc_furnaces_clogged = false;
            filterLocalGrid<IMyRefinery>(local_arc_furnaces);
            foreach (IMyRefinery f in local_arc_furnaces)
            {
                if (!f.IsQueueEmpty && !f.IsProducing)
                {
                    addBlockAlert(f, ALERT_CLOGGED);
                    arc_furnaces_clogged = true;
                }
                else
                {
                    removeBlockAlert(f, ALERT_CLOGGED);
                }
                updateBlockName(f);
            }
            if (!null_list.Contains(local_arc_furnaces_subset))
            {
                local_arc_furnaces_subset = randomSubset(local_arc_furnaces, 40);
            }
            return local_arc_furnaces_subset;
        }

        List<IMyTerminalBlock> getAllRefineries()
        {
            if (local_all_refineries == null)
            {
                local_all_refineries = new List<IMyTerminalBlock>();
            }
            if (!null_list.Contains(local_all_refineries))
            {
                local_all_refineries.Clear();
                local_all_refineries.AddRange(getRefineries());
                local_all_refineries.AddRange(getArcFurnaces());
            }
            return removeNulls(local_all_refineries);
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
                    var input_inv = a.GetInventory(0);
                    var output_inv = a.GetInventory(1);
                    float input_load = (float)input_inv.CurrentVolume / (float)input_inv.MaxVolume;
                    float output_load = (float)output_inv.CurrentVolume / (float)output_inv.MaxVolume;
                    bool isWaiting = !a.IsQueueEmpty && !a.IsProducing;
                    removeBlockAlert(a, ALERT_MATERIALS_MISSING);
                    removeBlockAlert(a, ALERT_CLOGGED);
                    if ((input_load > 0.98F || output_load > 0.98F) && isWaiting)
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
            if (local_text_panels != null && !force_update)
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
            local_arc_furnaces = new List<IMyTerminalBlock>();
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
                    // refineries and furnaces are of the same type, but differ in definitions
                    if (b.BlockDefinition.ToString().Contains("LargeRefinery"))
                    {
                        local_refineries.Add(b);
                    }
                    else
                    {
                        local_arc_furnaces.Add(b);
                    }
                }
                else if (b is IMyAssembler)
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
                    data.has_drills = true;
                }
                else if (b is IMyShipGrinder)
                {
                    local_grinders.Add(b);
                    data.has_grinders = true;
                }
                else if (b is IMyShipWelder)
                {
                    local_welders.Add(b);
                    data.has_welders = true;
                }
                else if (b is IMyAirVent)
                {
                    local_air_vents.Add(b);
                }
                else if (b is IMyGasTank)
                {
                    // oxygen and hydrogen tanks are of the same type, but differ in definitions
                    if (b.BlockDefinition.ToString().Contains("Hydrogen"))
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
                    data.has_wheels = true;
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
                    data.has_thrusters = true;
                }
                else if (b is IMyProgrammableBlock && b != Me && b.CubeGrid != Me.CubeGrid)
                {
                    // skip disabled CPU's as well
                    if (b.CustomName == "BARABAS Ship CPU")
                    {
                        data.override_ship = true;
                    }
                    else if (b.CustomName == "BARABAS Base CPU")
                    {
                        data.override_base = true;
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
                local_grid_data.has_wheels |= tmp_grid_data[grid].has_wheels;
                local_grid_data.has_thrusters |= tmp_grid_data[grid].has_thrusters;
                local_grid_data.has_drills |= tmp_grid_data[grid].has_drills;
                local_grid_data.has_grinders |= tmp_grid_data[grid].has_grinders;
                local_grid_data.has_welders |= tmp_grid_data[grid].has_welders;
                local_grid_data.override_base |= tmp_grid_data[grid].override_base;
                local_grid_data.override_ship |= tmp_grid_data[grid].override_ship;
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
                            data.has_wheels |= tmp_grid_data[g].has_wheels;
                            data.has_thrusters |= tmp_grid_data[g].has_thrusters;
                            data.has_drills |= tmp_grid_data[g].has_drills;
                            data.has_grinders |= tmp_grid_data[g].has_grinders;
                            data.has_welders |= tmp_grid_data[g].has_welders;
                            data.override_base |= tmp_grid_data[g].override_base;
                            data.override_ship |= tmp_grid_data[g].override_ship;
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
                if (data.override_ship)
                {
                    s_g.Add(grid);
                    continue;
                }
                else if (data.override_base)
                {
                    base_grid_info.Add(data);
                    b_g.Add(grid);
                    continue;
                }
                // if we're a base, assume every other grid is a ship unless we're explicitly
                // told that it's another base
                if (data.has_thrusters || data.has_wheels || isBaseMode())
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
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyGasTank>(blocks, remoteGridFilter);
            float o_level = 0;
            float h_level = 0;
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i] as IMyGasTank;
                if (b.Stockpile || slimBlock(b) == null)
                {
                    continue;
                }

                bool sz = b.BlockDefinition.ToString().Contains("Large");

                if (b.BlockDefinition.ToString().Contains("Hydrogen"))
                {
                    h_level += b.FilledRatio * (sz ? 2500000 : 40000);
                }
                else
                {
                    o_level += b.FilledRatio * (sz ? 100000 : 50000);
                }
            }
            // if we have at least half a tank, we can refuel
            can_refuel_hydrogen = h_level > (large_grid ? 1250000 : 20000);
            can_refuel_oxygen = o_level > (large_grid ? 50000 : 25000);
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

            // notify user if there are old-style BARABAS Trash blocks
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName("BARABAS Trash", blocks, localGridFilter);

            foreach (var b in blocks)
            {
                if (!local_trash_connectors.Contains(b))
                {
                    b.CustomName = b.CustomName + " [BARABAS: Deprecated]";
                    b.ShowOnHUD = true;
                }
            }

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

            // notify user if there are old-style BARABAS trash sensors
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName("BARABAS Trash Sensor", blocks, localGridFilter);

            foreach (var b in blocks)
            {
                if (!local_trash_sensors.Contains(b))
                {
                    b.CustomName = b.CustomName + " [BARABAS: Deprecated]";
                    b.ShowOnHUD = true;
                }
            }

            return local_trash_sensors;
        }

        IMyTextPanel getConfigBlock(bool force_update = false)
        {
            if (!force_update && config_block != null)
            {
                if (!blockExists(config_block))
                {
                    return null;
                }
                return config_block;
            }
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName("BARABAS Config", blocks, localGridFilter);
            if (blocks.Count < 1)
            {
                return null;
            }
            else if (blocks.Count > 1)
            {
                Echo("Multiple config blocks found.");
                if (config_block != null)
                {
                    // find our previous config block, ignore the rest
                    var id = config_block.EntityId;
                    config_block = GridTerminalSystem.GetBlockWithId(id) as IMyTextPanel;
                }
                if (config_block == null)
                {
                    // if we didn't find our config block, just use the first one
                    config_block = blocks[0] as IMyTextPanel;
                }
            }
            else
            {
                config_block = blocks[0] as IMyTextPanel;
            }
            return config_block;
        }

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
            var items = inv.GetItems();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!isIngot(item))
                {
                    continue;
                }
                if (name != null && item.Content.SubtypeName != name)
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
            var items = inv.GetItems();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!isOre(item))
                {
                    continue;
                }
                if (name != null && item.Content.SubtypeName != name)
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
            List<ItemHelper> list = new List<ItemHelper>();
            var blocks = getStorage();
            foreach (var b in blocks)
            {
                getAllOre(b, 0, name, list);
            }
            return list;
        }

        List<ItemHelper> getAllStorageIngots(string name = null)
        {
            List<ItemHelper> list = new List<ItemHelper>();
            var blocks = getStorage();
            foreach (var b in blocks)
            {
                getAllIngots(b, 0, name, list);
            }
            return list;
        }

        bool isOre(IMyInventoryItem i)
        {
            if (i.Content.SubtypeName == "Scrap")
            {
                return true;
            }
            return i.Content.TypeId.ToString().Equals("MyObjectBuilder_Ore");
        }

        bool isIngot(IMyInventoryItem i)
        {
            if (i.Content.SubtypeName == "Scrap")
            {
                return false;
            }
            return i.Content.TypeId.ToString().Equals("MyObjectBuilder_Ingot");
        }

        bool isComponent(IMyInventoryItem i)
        {
            return i.Content.TypeId.ToString().Equals("MyObjectBuilder_Component");
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
            Dictionary<string, int> posmap = new Dictionary<string, int>();
            var items = inv.GetItems();
            bool needs_consolidation = false;
            // go through all items and note the first time they appear in the inventory
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string str = item.Content.TypeId.ToString() + item.Content.SubtypeName;
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
                string str = item.Content.TypeId.ToString() + item.Content.SubtypeName;
                int dstIndex = posmap[str];
                inv.TransferItemTo(inv, i, dstIndex, true, item.Amount);
            }
        }

        // make sure we process ore in chunks, prevent one ore clogging the refinery
        void rebalance(IMyInventory inv)
        {
            // make note of how much was the first item
            float? first_amount = null;
            if (inv.GetItems().Count > 1)
            {
                first_amount = (float)Math.Min((float)inv.GetItems()[0].Amount, CHUNK_SIZE);
            }
            consolidate(inv);
            var items = inv.GetItems();
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
            var src_items = src_inv.GetItems();
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

            src_items = src_inv.GetItems();

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
            src.TransferItemTo(src, srcIndex, src.GetItems().Count, true, amount);
        }

        void pushFront(IMyInventory src, int srcIndex, VRage.MyFixedPoint? amount)
        {
            src.TransferItemTo(src, srcIndex, 0, true, amount);
        }

        /**
         * Volume & storage load functions
         */
        float getTotalStorageLoad()
        {
            var s = getStorage();

            float cur_volume = 0;
            float max_volume = 0;
            float ratio;
            foreach (var c in s)
            {
                cur_volume += (float)c.GetInventory(0).CurrentVolume;
                max_volume += (float)c.GetInventory(0).MaxVolume;
            }
            ratio = (float)Math.Round(cur_volume / max_volume, 2);

            if (isSpecializedShipMode())
            {
                ratio = (float)Math.Round(ratio * 0.75F, 4);
            }
            else
            {
                return ratio;
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
            float maxLoad = 0;
            foreach (var c in s)
            {
                var inv = c.GetInventory(0);
                var load = (float)inv.CurrentVolume / (float)inv.MaxVolume;
                if (load > maxLoad)
                {
                    maxLoad = load;
                }
            }
            // scale the drill/grinder load to fit in the last 25% of the storage
            // the result of this is, when the storage is full, yellow alert goes off,
            // when drills/grinders are full, red alert goes off
            ratio = ratio + maxLoad * 0.25F;
            return ratio;
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
            float volumeLeft = ((float)inv.MaxVolume - (float)inv.CurrentVolume) * 1000;
            if (volumeLeft > 1500)
            {
                return true;
            }
            if (volumeLeft < 100 || name == ICE)
            {
                return false;
            }

            // if this is a priority ore, accept it unconditionally
            if (can_use_ingots && storage_ingot_status[name] < material_thresholds[name])
            {
                return true;
            }
            // if no ore is priority, don't clog the refinery
            if (volumeLeft < 600)
            {
                return false;
            }

            // aim for equal spread
            var ores = new Dictionary<string, float>();
            var items = inv.GetItems();
            bool seenCurrent = false;
            foreach (var i in items)
            {
                var ore = i.Content.SubtypeName;
                float amount;
                ores.TryGetValue(ore, out amount);
                ores[ore] = amount + (float)i.Amount;
                if (ore == name)
                {
                    seenCurrent = true;
                }
            }
            int keyCount = ores.Keys.Count;
            if (!seenCurrent)
            {
                keyCount++;
            }
            // don't clog refinery with single ore
            if (keyCount < 2)
            {
                if (volumeLeft < 1000)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            float cur_amount;
            ores.TryGetValue(name, out cur_amount);
            float target_amount = ((float)inv.CurrentVolume * 1000) / keyCount;
            return cur_amount < target_amount;
        }

        bool hasOnlyOre(IMyInventory inv)
        {
            var items = inv.GetItems();
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
            var items = inv.GetItems();
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
            var items = inv.GetItems();
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
                removeAlert(AlertLevel.YELLOW_ALERT);
                removeAlert(AlertLevel.RED_ALERT);
                return;
            }
            float storageLoad = getTotalStorageLoad();
            if (storageLoad >= 0.98F)
            {
                addAlert(AlertLevel.RED_ALERT);
                removeAlert(AlertLevel.YELLOW_ALERT);
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
                removeAlert(AlertLevel.RED_ALERT);
            }

            if (crisis_mode == CrisisMode.CRISIS_MODE_THROW_ORE && storageLoad < 0.98F)
            {
                // exit crisis mode, but "tried_throwing" still reminds us that we
                // have just thrown out ore - if we end up in a crisis again, we'll
                // go lockup instead of throwing ore
                crisis_mode = CrisisMode.CRISIS_MODE_NONE;
                tried_throwing = true;
                storeTrash(true);
            }
            if (storageLoad >= 0.75F && storageLoad < 0.98F)
            {
                storeTrash();
                addAlert(AlertLevel.YELLOW_ALERT);
            }
            else if (storageLoad < 0.75F)
            {
                removeAlert(AlertLevel.YELLOW_ALERT);
                storeTrash();
                if (storageLoad < 0.98F && isBaseMode())
                {
                    tried_throwing = false;
                }
            }
            float mass = getTotalStorageMass();
            int idx = 0;
            string suffixes = " kMGTPEZY";
            while (mass >= 1000)
            {
                mass /= 1000;
                idx++;
            }
            mass = (float)Math.Round(mass, 1);
            char suffix = suffixes[idx];

            status_report[STATUS_STORAGE_LOAD] = String.Format("{0}% / {1}{2}",
            (float)Math.Round(storageLoad * 100, 0), mass, suffix);
        }

        string getPowerLoadStr(float value)
        {
            var pwrs = "kMGTPEZY";
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
            int sorted_items = 0;

            // for each container, transfer one item
            for (int c = 0; c < s.Count; c++)
            {
                var container = s[c];
                var inv = container.GetInventory(0);
                var items = inv.GetItems();
                var curVolume = (float)inv.CurrentVolume;

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
                    if (curVolume != (float)inv.CurrentVolume)
                    {
                        sorted_items++;
                        curVolume = (float)inv.CurrentVolume;
                    }
                    // sort ten items per run
                    if (sorted_items == 10)
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
            var item = src.GetItems()[srcIndex];
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
            float maxFreeVolume = 0;
            for (int i = 0; i < c.Count; i++)
            {
                // skip containers we already saw in a previous loop
                if (i % steps == startStep)
                {
                    continue;
                }

                // skip full containers
                var container_inv = c[i].GetInventory(0);
                float freeVolume = ((float)container_inv.MaxVolume - (float)container_inv.CurrentVolume) * 1000;
                if (freeVolume < 1)
                {
                    continue;
                }

                if (emptyIdx == -1 && (float)container_inv.CurrentVolume == 0)
                {
                    emptyIdx = i;
                    continue;
                }
                if (overflowIdx == -1)
                {
                    bool isOverflow = false;
                    if (itemIsOre && hasOnlyOre(container_inv))
                    {
                        isOverflow = true;
                    }
                    else if (itemIsIngot && hasOnlyIngots(container_inv))
                    {
                        isOverflow = true;
                    }
                    else if (itemIsComponent && hasOnlyComponents(container_inv))
                    {
                        isOverflow = true;
                    }
                    if (isOverflow)
                    {
                        overflowIdx = i;
                        continue;
                    }
                }
                if (freeVolume > maxFreeVolume)
                {
                    leastFullIdx = i;
                    maxFreeVolume = freeVolume;
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
                var container_inv = c.GetInventory(0);
                float freeVolume = ((float)container_inv.MaxVolume - (float)container_inv.CurrentVolume) * 1000;
                if (freeVolume < 1)
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
                var items = inv.GetItems();
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && push_ore_to_base)
                    {
                        pushToRemoteStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var t = i.Content.SubtypeName;
                        if (t != URANIUM && push_ingots_to_base)
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
                var items = c.GetInventory(0).GetItems();
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && pull_ore_from_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Content.SubtypeName;
                        // don't take all uranium from base
                        if (type == URANIUM && auto_refuel_ship && !powerAboveHighWatermark())
                        {
                            pushToStorage(c, 0, j, (VRage.MyFixedPoint)Math.Min(0.5F, (float)i.Amount));
                        }
                        else if (type != URANIUM && pull_ingots_from_base)
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
                var items = inv.GetItems();
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && pull_ore_from_base)
                    {
                        pushToRemoteShipStorage(s, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Content.SubtypeName;
                        if (type != URANIUM && pull_ingots_from_base)
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
            var storage = getRemoteShipStorage();
            foreach (var c in storage)
            {
                var inv = c.GetInventory(0);
                var items = inv.GetItems();
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    var i = items[j];
                    if (isOre(i) && push_ore_to_base)
                    {
                        pushToStorage(c, 0, j, null);
                    }
                    if (isIngot(i))
                    {
                        var type = i.Content.SubtypeName;
                        // don't take all uranium from base
                        if (type != URANIUM && push_ingots_to_base)
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
        void emptyBlocks(List<IMyTerminalBlock> blocks)
        {
            foreach (var b in blocks)
            {
                var inv = b.GetInventory(0);
                var items = inv.GetItems();
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
                float target_volume = max_vol - 400 - cur_vol;
                if (target_volume <= 0)
                {
                    continue;
                }
                var dst_inv = w.GetInventory(0);
                var storage = getStorage();
                for (; s_index < storage.Count; s_index++)
                {
                    var c = storage[s_index];
                    var src_inv = c.GetInventory(0);
                    var items = src_inv.GetItems();
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        var i = items[j];
                        if (!isComponent(i))
                        {
                            continue;
                        }
                        if (target_volume <= 0)
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
                        if (!Transfer(w, 0, c, 0, j, null, true, (VRage.MyFixedPoint)1))
                        {
                            continue;
                        }
                        float new_vol = (float)dst_inv.CurrentVolume * 1000;
                        float item_vol = new_vol - old_vol;
                        int target_amount = (int)Math.Floor(target_volume / item_vol);
                        src_inv.TransferItemTo(dst_inv, j, null, true, (VRage.MyFixedPoint)target_amount);
                        target_volume -= (float)Math.Min(target_amount, amount) * item_vol;
                    }
                    if (target_volume <= 0)
                    {
                        break;
                    }
                }
            }
        }

        // push all ore from refineries to storage
        void pushOreToStorage()
        {
            var rs = getAllRefineries();
            foreach (var r in rs)
            {
                var inv = r.GetInventory(0);
                for (int j = inv.GetItems().Count - 1; j >= 0; j--)
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
                for (int j = inv.GetItems().Count - 1; j >= 0; j--)
                {
                    var i = inv.GetItems()[j];
                    if (i.Content.SubtypeName != ICE)
                    {
                        continue;
                    }
                    pushToStorage(r, 0, j, null);
                }
            }
        }

        /**
         * Uranium, reactors & batteries
         */
        float getMaxReactorPowerOutput(bool force_update = false)
        {
            if (!force_update)
            {
                return max_reactor_output;
            }

            max_reactor_output = 0;
            var reactors = getReactors();
            foreach (IMyReactor r in reactors)
            {
                max_reactor_output += r.MaxOutput * 1000;
            }

            return max_reactor_output;
        }

        float getCurReactorPowerOutput(bool force_update = false)
        {
            if (!force_update)
            {
                return cur_reactor_output;
            }

            cur_reactor_output = 0;
            var reactors = getReactors();
            foreach (IMyReactor r in reactors)
            {
                if (r.IsWorking)
                {
                    cur_reactor_output += r.MaxOutput * 1000;
                }
            }

            return cur_reactor_output;
        }

        float getMaxBatteryPowerOutput(bool force_update = false)
        {
            if (!force_update)
            {
                return max_battery_output;
            }

            max_battery_output = 0;
            var batteries = getBatteries();
            foreach (IMyBatteryBlock b in batteries)
            {
                if (b.HasCapacityRemaining)
                {
                    // there's no API function to provide this information, and parsing
                    // DetailedInfo is kinda overkill for this, so just hard-code the value
                    max_battery_output += large_grid ? 12000 : 4320;
                }
            }

            return max_battery_output;
        }

        float getBatteryStoredPower()
        {
            var batteries = getBatteries();
            float stored_power = 0;
            foreach (IMyBatteryBlock b in batteries)
            {
                // unlike reactors, batteries' kWh are _actual_ kWh, not kWm
                stored_power += b.CurrentStoredPower * 1000 * 60;
            }
            return stored_power;
        }

        float getReactorStoredPower()
        {
            if (has_reactors)
            {
                return URANIUM_INGOT_POWER * ingot_status[URANIUM];
            }
            return 0;
        }

        // since blocks don't report their power draw, we look at what reactors/batteries
        // are outputting instead. we don't count solar as those are transient power sources
        float getCurPowerDraw(bool force_update = false)
        {
            if (!force_update)
            {
                return cur_power_draw;
            }

            float power_draw = 0;

            // go through all reactors and batteries
            foreach (IMyReactor b in getReactors())
            {
                power_draw += b.CurrentOutput * 1000;
            }
            foreach (IMyBatteryBlock b in getBatteries())
            {
                power_draw += (b.CurrentOutput - b.CurrentInput) * 1000;
            }

            cur_power_draw = power_draw;

            return cur_power_draw;
        }

        // blocks don't report their max power draw, so we're forced to parse DetailedInfo
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
                // if this is a thruster
                if (b is IMyThrust)
                {
                    var typename = b.BlockDefinition.ToString();
                    float thrust_draw;
                    bool found = thrust_power.TryGetValue(typename, out thrust_draw);
                    if (found)
                    {
                        power_draw += thrust_draw;
                    }
                    else
                    {
                        thrust_draw = getBlockPowerUse(b);
                        if (thrust_draw == 0)
                        {
                            throw new BarabasException("Unknown thrust type", this);
                        }
                        else
                        {
                            power_draw += thrust_draw;
                        }
                    }
                }
                // it's a regular block
                else
                {
                    power_draw += getBlockPowerUse(b);
                }
            }
            // add 10% to account for various misc stuff like conveyors etc
            power_draw *= 1.1F;

            if (getMaxBatteryPowerOutput() + getCurReactorPowerOutput() == 0)
            {
                max_power_draw = power_draw;
            }
            else
            {
                // now, check if we're not overflowing the reactors and batteries
                max_power_draw = (float)Math.Min(power_draw, getMaxBatteryPowerOutput() + getCurReactorPowerOutput());
            }

            return max_power_draw;
        }

        // parse DetailedInfo for power use information - this shouldn't exist, but
        // the API is deficient, sooo...
        float getBlockPowerUse(IMyTerminalBlock b)
        {
            var power_regex = new System.Text.RegularExpressions.Regex("Max Required Input: ([\\d\\.]+) (\\w?)W");
            var cur_regex = new System.Text.RegularExpressions.Regex("Current Input: ([\\d\\.]+) (\\w?)W");
            var power_match = power_regex.Match(b.DetailedInfo);
            var cur_match = cur_regex.Match(b.DetailedInfo);
            if (!power_match.Success && !cur_match.Success)
            {
                return 0;
            }

            float cur = 0, max = 0;
            if (power_match.Groups[1].Success && power_match.Groups[2].Success)
            {
                bool result = float.TryParse(power_match.Groups[1].Value, out max);
                if (!result)
                {
                    throw new BarabasException("Invalid detailed info format!", this);
                }
                max *= (float)Math.Pow(1000, " kMGTPEZY".IndexOf(power_match.Groups[2].Value) - 1);
            }
            if (cur_match.Groups[1].Success && cur_match.Groups[2].Success)
            {
                bool result = float.TryParse(cur_match.Groups[1].Value, out cur);
                if (!result)
                {
                    throw new BarabasException("Invalid detailed info format!", this);
                }
                cur *= (float)Math.Pow(1000, " kMGTPEZY".IndexOf(cur_match.Groups[2].Value) - 1);
            }
            return Math.Max(cur, max);
        }

        float getPowerHighWatermark(float power_use)
        {
            return power_use * power_high_watermark;
        }

        float getPowerLowWatermark(float power_use)
        {
            return power_use * power_low_watermark;
        }

        bool powerAboveHighWatermark()
        {
            var stored_power = getBatteryStoredPower() + getReactorStoredPower();

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
            float totalPowerNeeded = power_needed * 1.3F;

            if (stored_power > totalPowerNeeded)
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
            return getBatteryStoredPower() + getReactorStoredPower() > getPowerLowWatermark(power_draw);
        }

        // push uranium into reactors, optionally push ALL uranium into reactors
        bool refillReactors(bool force = false)
        {
            bool refilled = true;
            ItemHelper ingot = null;
            float orig_amount = 0, cur_amount = 0;
            int s_index = 0;
            // check if we can put some more uranium into reactors
            var reactors = getReactors();
            foreach (IMyReactor reactor in reactors)
            {
                var rinv = reactor.GetInventory(0);
                float r_proportion = reactor.MaxOutput * 1000 / getMaxReactorPowerOutput();
                float r_power_draw = getMaxPowerDraw() * (r_proportion);
                float ingots_per_reactor = getPowerHighWatermark(r_power_draw) / URANIUM_INGOT_POWER;
                float ingots_in_reactor = getTotalIngots(reactor, 0, URANIUM);
                if ((ingots_in_reactor < ingots_per_reactor) || force)
                {
                    // find us an ingot
                    if (ingot == null)
                    {
                        var storage = getStorage();
                        for (; s_index < storage.Count; s_index++)
                        {
                            var sinv = storage[s_index].GetInventory(0);
                            var items = sinv.GetItems();
                            for (int j = 0; j < items.Count; j++)
                            {
                                var item = items[j];
                                if (isIngot(item) && item.Content.SubtypeName == URANIUM)
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
                    float amount;
                    float p_amount = (float)Math.Round(orig_amount * r_proportion, 4);
                    if (force)
                    {
                        amount = p_amount;
                    }
                    else
                    {
                        amount = (float)Math.Min(p_amount, ingots_per_reactor - ingots_in_reactor);
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
                        amount = TryTransfer(ingot.Owner, ingot.InvIdx, reactor, 0, ingot.Index, null, true, (VRage.MyFixedPoint)amount);
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
            var reactors = getReactors();
            foreach (IMyReactor r in reactors)
            {
                var inv = r.GetInventory(0);
                if (inv.GetItems().Count > 1)
                {
                    consolidate(inv);
                }
                float ingots = getTotalIngots(r, 0, URANIUM);
                float r_power_draw = getMaxPowerDraw() *
                    ((r.MaxOutput * 1000) / (getMaxReactorPowerOutput() + getMaxBatteryPowerOutput()));
                float ingots_per_reactor = getPowerHighWatermark(r_power_draw);
                if (ingots > ingots_per_reactor)
                {
                    float amount = ingots - ingots_per_reactor;
                    pushToStorage(r, 0, 0, (VRage.MyFixedPoint)amount);
                }
            }
        }

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

        // only stone is considered unuseful, and only if config says to throw out stone
        bool isUseful(IMyInventoryItem item)
        {
            return !throw_out_stone || item.Content.SubtypeName != STONE;
        }

        bool hasUsefulItems(IMyShipConnector connector)
        {
            var inv = connector.GetInventory(0);
            foreach (var item in inv.GetItems())
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

        // move everything (or everything excluding stone) from trash to storage
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
                var items = inv.GetItems();
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
                string ore = item.Item.Content.SubtypeName;
                if (ore == SCRAP)
                {
                    ore = IRON;
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
                else if (arc_furnace_ores.Contains(ore))
                {
                    rs = getAllRefineries();
                }
                else
                {
                    rs = getRefineries();
                }
                if (rs.Count == 0)
                {
                    continue;
                }

                float orig_amount = (float)Math.Round((float)item.Item.Amount / rs.Count, 4);
                float amount = (float)Math.Min(CHUNK_SIZE, orig_amount);
                // now, go through every refinery and do the transfer
                for (int r = 0; r < rs.Count; r++)
                {
                    // if we're last in the list, send it all
                    if (r == rs.Count - 1 && amount < CHUNK_SIZE)
                    {
                        amount = 0;
                    }
                    var refinery = rs[r];
                    removeBlockAlert(refinery, ALERT_CLOGGED);
                    var input_inv = refinery.GetInventory(0);
                    var output_inv = refinery.GetInventory(1);
                    float input_load = (float)input_inv.CurrentVolume / (float)input_inv.MaxVolume;
                    if (canAcceptOre(input_inv, ore) || ore == ICE)
                    {
                        // if we've got a very small amount, send it all
                        var item_inv = item.Owner.GetInventory(item.InvIdx);
                        if (amount < 1)
                        {
                            if (Transfer(item.Owner, item.InvIdx, refinery, 0, item.Index, input_inv.GetItems().Count, true, null))
                            {
                                break;
                            }
                        }
                        // if refinery is almost empty, send a lot
                        else if (input_load < 0.2F)
                        {
                            amount = (float)Math.Min(CHUNK_SIZE * 5, orig_amount);
                            item_inv.TransferItemTo(input_inv, item.Index, input_inv.GetItems().Count, true, (VRage.MyFixedPoint)amount);
                        }
                        else
                        {
                            item_inv.TransferItemTo(input_inv, item.Index, input_inv.GetItems().Count, true, (VRage.MyFixedPoint)amount);
                        }
                    }
                }
            }
        }

        // find which ore needs prioritization the most
        void reprioritizeOre()
        {
            string low_wm_ore = null;
            string high_wm_ore = null;
            string low_wm_arc_ore = null;
            string high_wm_arc_ore = null;

            // if we know we want uranium, prioritize it
            if (prioritize_uranium && ore_status[URANIUM] > 0)
            {
                low_wm_ore = URANIUM;
            }
            // find us ore to prioritize (hi and low, regular and arc furnace)
            foreach (var ore in ore_types)
            {
                if (ore == ICE)
                {
                    // ice is refined separately
                    continue;
                }
                bool arc = arc_furnace_ores.Contains(ore);
                if (low_wm_ore == null && storage_ingot_status[ore] < material_thresholds[ore] && ore_status[ore] > 0)
                {
                    low_wm_ore = ore;
                }
                else if (high_wm_ore == null && storage_ingot_status[ore] < (material_thresholds[ore] * 5) && ore_status[ore] > 0)
                {
                    high_wm_ore = ore;
                }
                if (arc && low_wm_arc_ore == null && storage_ingot_status[ore] < material_thresholds[ore] && ore_status[ore] > 0)
                {
                    low_wm_arc_ore = ore;
                }
                else if (arc && high_wm_arc_ore == null && storage_ingot_status[ore] < (material_thresholds[ore] * 5) && ore_status[ore] > 0)
                {
                    high_wm_arc_ore = ore;
                }
                if (high_wm_ore != null && low_wm_ore != null && high_wm_arc_ore != null && low_wm_arc_ore != null)
                {
                    break;
                }
            }
            // now, reorder ore in refineries
            if (high_wm_ore != null || low_wm_ore != null)
            {
                var ore = low_wm_ore != null ? low_wm_ore : high_wm_ore;
                List<IMyTerminalBlock> rs;
                rs = getRefineries();
                foreach (var refinery in rs)
                {
                    var inv = refinery.GetInventory(0);
                    var items = inv.GetItems();
                    for (int j = 0; j < items.Count; j++)
                    {
                        var cur = items[j].Content.SubtypeName;
                        if (cur == SCRAP)
                        {
                            cur = IRON;
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
            // reorder ore in arc furnaces
            if (high_wm_arc_ore != null || low_wm_arc_ore != null)
            {
                var ore = low_wm_arc_ore != null ? low_wm_arc_ore : high_wm_arc_ore;
                List<IMyTerminalBlock> rs;
                rs = getArcFurnaces();
                foreach (var refinery in rs)
                {
                    var inv = refinery.GetInventory(0);
                    var items = inv.GetItems();
                    for (int j = 0; j < items.Count; j++)
                    {
                        var cur = items[j].Content.SubtypeName;
                        if (cur == SCRAP)
                        {
                            cur = IRON;
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
            public int minIndex;
            public int maxIndex;
            public float minLoad;
            public float maxLoad;
            public int minArcIndex;
            public int maxArcIndex;
            public float minArcLoad;
            public float maxArcLoad;
        }

        // go through a list of blocks and find most and least utilized
        RebalanceResult findMinMax(List<IMyTerminalBlock> blocks)
        {
            RebalanceResult r = new RebalanceResult();
            int minI = 0, maxI = 0, minAI = 0, maxAI = 0;
            float minL = float.MaxValue, maxL = 0, minAL = float.MaxValue, maxAL = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                var inv = b.GetInventory(0);
                rebalance(inv);
                float arcload = 0;
                var items = inv.GetItems();
                foreach (var item in items)
                {
                    var name = item.Content.SubtypeName;
                    if (name == SCRAP)
                    {
                        name = IRON;
                    }
                    if (arc_furnace_ores.Contains(name))
                    {
                        arcload += (float)item.Amount * VOLUME_ORE;
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
                if (arcload < minAL)
                {
                    minAI = i;
                    minAL = arcload;
                }
                if (arcload > maxAL)
                {
                    maxAI = i;
                    maxAL = arcload;
                }
            }
            r.minIndex = minI;
            r.maxIndex = maxI;
            r.minLoad = minL;
            r.maxLoad = maxL;
            r.minArcIndex = minAI;
            r.maxArcIndex = maxAI;
            r.minArcLoad = minAL;
            r.maxArcLoad = maxAL;
            return r;
        }

        // spread ore between two inventories
        bool spreadOre(IMyTerminalBlock src, int srcIdx, IMyTerminalBlock dst, int dstIdx)
        {
            bool success = false;

            var src_inv = src.GetInventory(srcIdx);
            var dst_inv = dst.GetInventory(dstIdx);
            var maxLoad = (float)src_inv.CurrentVolume * 1000;
            var minLoad = (float)dst_inv.CurrentVolume * 1000;

            var items = src_inv.GetItems();
            var target_volume = (float)(maxLoad - minLoad) / 2;
            // spread all ore equally
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var volume = items[i].Content.SubtypeName == SCRAP ? VOLUME_SCRAP : VOLUME_ORE;
                var cur_amount = (float)items[i].Amount;
                var cur_vol = (cur_amount * volume) / 2;
                float amount = (float)Math.Min(target_volume, cur_vol) / volume;
                VRage.MyFixedPoint? tmp = (VRage.MyFixedPoint)amount;
                // if there's peanuts, send it all
                if (cur_amount < 250)
                {
                    tmp = null;
                    amount = (float)items[i].Amount;
                }
                amount = TryTransfer(src, srcIdx, dst, dstIdx, i, null, true, tmp);
                if (amount > 0)
                {
                    success = true;
                    target_volume -= amount * volume;
                    if (target_volume <= 0)
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

        // go through all refineries, arc furnaces and oxygen generators, and find
        // least and most utilized, and spread load between them
        void rebalanceRefineries()
        {
            bool refsuccess = false;
            bool arcsuccess = false;
            var ratio = 1.25F;

            // balance oxygen generators
            var ogs = getOxygenGenerators();
            RebalanceResult oxyresult = findMinMax(ogs);

            if (oxyresult.maxLoad > 0)
            {
                bool trySpread = oxyresult.minLoad == 0 || oxyresult.maxLoad / oxyresult.minLoad > ratio;
                if (oxyresult.minIndex != oxyresult.maxIndex && trySpread)
                {
                    var src = ogs[oxyresult.maxIndex];
                    var dst = ogs[oxyresult.minIndex];
                    spreadOre(src, 0, dst, 0);
                }
            }

            // balance refineries and arc furnaces separately
            var rs = getRefineries();
            var furnaces = getArcFurnaces();
            RebalanceResult refresult = findMinMax(rs);
            RebalanceResult arcresult = findMinMax(furnaces);

            if (refresult.maxLoad > 250)
            {
                bool trySpread = refresult.minLoad == 0 || refresult.maxLoad / refresult.minLoad > ratio;
                if (refresult.minIndex != refresult.maxIndex && trySpread)
                {
                    var src = rs[refresult.maxIndex];
                    var dst = rs[refresult.minIndex];
                    if (spreadOre(src, 0, dst, 0))
                    {
                        refsuccess = true;
                    }
                }
            }
            if (arcresult.maxLoad > 250)
            {
                bool trySpread = arcresult.minLoad == 0 || (arcresult.maxLoad / arcresult.minLoad) > ratio;
                if (arcresult.minIndex != arcresult.maxIndex && trySpread)
                {
                    var src = furnaces[arcresult.maxIndex];
                    var dst = furnaces[arcresult.minIndex];
                    if (spreadOre(src, 0, dst, 0))
                    {
                        arcsuccess = true;
                    }
                }
            }

            if (rs.Count == 0 || furnaces.Count == 0 || arcsuccess)
            {
                return;
            }

            // cross pollination: spread load from ref to arc
            float refToArcRatio = 0;
            if (arcresult.minLoad != 0)
            {
                refToArcRatio = refresult.maxArcLoad / arcresult.minLoad;
            }
            bool refToArc = !refsuccess || (refsuccess && refresult.maxIndex != refresult.maxArcIndex);
            refToArc = refToArcRatio > ratio || (arcresult.minLoad == 0 && refresult.maxArcLoad > 0);
            if (refToArc)
            {
                var src = rs[refresult.maxArcIndex];
                var dst = furnaces[arcresult.minIndex];
                if (spreadOre(src, 0, dst, 0))
                {
                    return;
                }
            }

            // spread load from arc to ref
            float arcToRefRatio = 0;
            if (refresult.minLoad != 0)
            {
                arcToRefRatio = arcresult.maxLoad / refresult.minLoad;
            }

            bool arcToRef = refresult.minLoad == 0 || arcToRefRatio > ratio;
            if (arcToRef)
            {
                var src = furnaces[arcresult.maxIndex];
                var dst = rs[refresult.minIndex];
                spreadOre(src, 0, dst, 0);
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
                if (ore == URANIUM)
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
            var rs = getAllRefineries();
            foreach (IMyRefinery r in rs)
            {
                if (r.IsQueueEmpty || r.IsProducing)
                {
                    return false;
                }
            }
            return true;
        }

        /**
         * Declog and spread load
         */
        void declogAssemblers()
        {
            var assemblers = getAssemblers();
            foreach (IMyAssembler assembler in assemblers)
            {
                var inv = assembler.GetInventory(0);

                // empty assembler input if it's not doing anything
                var items = inv.GetItems();
                if (assembler.IsQueueEmpty)
                {
                    items = inv.GetItems();
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        pushToStorage(assembler, 0, j, null);
                    }
                }

                inv = assembler.GetInventory(1);

                // empty output but only if it's not disassembling
                if (assembler.Mode != MyAssemblerMode.Disassembly)
                {
                    items = inv.GetItems();
                    for (int j = items.Count - 1; j >= 0; j--)
                    {
                        pushToStorage(assembler, 1, j, null);
                    }
                }
            }
        }

        void declogRefineries()
        {
            var rs = getAllRefineries();
            foreach (var r in rs)
            {
                var inv = r.GetInventory(1);
                var items = inv.GetItems();
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
            int minIndex = 0, maxIndex = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                float load = (float)blocks[i].GetInventory(0).CurrentVolume / (float)blocks[i].GetInventory(0).MaxVolume;
                if (load < minLoad)
                {
                    minIndex = i;
                    minLoad = load;
                    minVol = (float)blocks[i].GetInventory(0).CurrentVolume * 1000;
                }
                if (load > maxLoad)
                {
                    maxIndex = i;
                    maxLoad = load;
                    maxVol = (float)blocks[i].GetInventory(0).CurrentVolume * 1000;
                }
            }
            // even out the load between biggest loaded block
            if (minIndex != maxIndex && (minLoad == 0 || maxLoad / minLoad > 1.1F))
            {
                var src = blocks[maxIndex];
                var dst = blocks[minIndex];
                var src_inv = blocks[maxIndex].GetInventory(0);
                var dst_inv = blocks[minIndex].GetInventory(0);
                var target_volume = (maxVol - minVol) / 2;
                var items = src_inv.GetItems();
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (target_volume <= 0)
                    {
                        return;
                    }
                    float amount = (float)items[i].Amount - 1;
                    // if it's peanuts, just send out everything
                    if (amount < 1)
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
                    int target_amount = (int)Math.Floor(target_volume / item_vol);
                    src_inv.TransferItemTo(dst_inv, i, null, true, (VRage.MyFixedPoint)target_amount);
                    target_volume -= (float)Math.Min(target_amount, amount) * item_vol;
                }
            }
        }

        /**
         * Oxygen
         */
        void checkOxygenLeaks()
        {
            var blocks = getAirVents();
            bool alert = false;
            foreach (IMyAirVent vent in blocks)
            {
                if (vent.Status == VentStatus.Pressurizing && !vent.CanPressurize)
                {
                    addBlockAlert(vent, ALERT_OXYGEN_LEAK);
                    alert = true;
                }
                else
                {
                    removeBlockAlert(vent, ALERT_OXYGEN_LEAK);
                }
                updateBlockName(vent);
            }
            if (alert)
            {
                addAlert(AlertLevel.BROWN_ALERT);
            }
            else
            {
                removeAlert(AlertLevel.BROWN_ALERT);
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
            if (has_oxygen_tanks && oxygen_high_watermark > 0 && cur_oxygen_level < oxygen_high_watermark)
            {
                return false;
            }
            return true;
        }

        bool hydrogenAboveHighWatermark()
        {
            if (has_hydrogen_tanks && hydrogen_high_watermark > 0 && cur_hydrogen_level < hydrogen_high_watermark)
            {
                return false;
            }
            return true;
        }

        bool iceAboveHighWatermark()
        {
            return oxygenAboveHighWatermark() && hydrogenAboveHighWatermark();
        }

        float getStoredOxygen()
        {
            if (!can_refine_ice)
            {
                return 0;
            }
            int n_oxygen_tanks = getOxygenTanks().Count;
            float capacity = large_grid ? 100000 : 50000;
            float total_capacity = n_oxygen_tanks * capacity;
            float ice_to_oxygen_ratio = 9;
            float stored_oxygen = ore_status[ICE] * ice_to_oxygen_ratio;
            return stored_oxygen / total_capacity * 100;
        }

        float getStoredHydrogen()
        {
            if (!can_refine_ice)
            {
                return 0;
            }
            int n_hydrogen_tanks = getHydrogenTanks().Count;
            float capacity = large_grid ? 2500000 : 40000;
            float total_capacity = n_hydrogen_tanks * capacity;
            float ice_to_hydrogen_ratio = large_grid ? 9 : 4;
            float stored_hydrogen = ore_status[ICE] * ice_to_hydrogen_ratio;
            return stored_hydrogen / total_capacity * 100;
        }

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
            throw_out_stone = true;
            material_thresholds[STONE] = 5000;
            power_low_watermark = 0;
            power_high_watermark = 0;
            oxygen_high_watermark = 0;
            oxygen_low_watermark = 0;
            hydrogen_high_watermark = 0;
            hydrogen_low_watermark = 0;
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
            if (power_low_watermark == 0)
            {
                if (isBaseMode())
                {
                    power_low_watermark = 60;
                }
                else
                {
                    power_low_watermark = 10;
                }
            }
            if (power_high_watermark == 0)
            {
                if (isBaseMode())
                {
                    power_high_watermark = 480;
                }
                else
                {
                    power_high_watermark = 30;
                }
            }
            if (oxygen_low_watermark == 0 && oxygen_high_watermark == 0 && has_oxygen_tanks)
            {
                if (isBaseMode())
                {
                    oxygen_low_watermark = 10;
                }
                else
                {
                    oxygen_low_watermark = 15;
                }
            }
            if (oxygen_high_watermark == 0 && has_oxygen_tanks)
            {
                if (isBaseMode())
                {
                    oxygen_high_watermark = 30;
                }
                else
                {
                    oxygen_high_watermark = 60;
                }
            }
            if (hydrogen_low_watermark == 0 && hydrogen_high_watermark == 0 && has_hydrogen_tanks)
            {
                if (isBaseMode())
                {
                    hydrogen_low_watermark = 0;
                }
                else
                {
                    hydrogen_low_watermark = 15;
                }
            }
            if (hydrogen_high_watermark == 0 && has_hydrogen_tanks)
            {
                if (isBaseMode())
                {
                    hydrogen_high_watermark = 30;
                }
                else
                {
                    hydrogen_high_watermark = 70;
                }
            }
        }

        // select operation mode
        void selectOperationMode()
        {
            // if we found some thrusters or wheels, assume we're a ship
            if (local_grid_data.has_thrusters || local_grid_data.has_wheels)
            {
                // this is likely a drill ship
                if (local_grid_data.has_drills && !local_grid_data.has_welders && !local_grid_data.has_grinders)
                {
                    setMode(OP_MODE_DRILL);
                }
                // this is likely a welder ship
                else if (local_grid_data.has_welders && !local_grid_data.has_drills && !local_grid_data.has_grinders)
                {
                    setMode(OP_MODE_WELDER);
                }
                // this is likely a grinder ship
                else if (local_grid_data.has_grinders && !local_grid_data.has_drills && !local_grid_data.has_welders)
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
                    if (i == (int)AlertLevel.BLUE_ALERT)
                    {
                        addAntennaAlert(ALERT_LOW_POWER);
                    }
                    else if (i == (int)AlertLevel.YELLOW_ALERT)
                    {
                        addAntennaAlert(ALERT_LOW_STORAGE);
                    }
                    else if (i == (int)AlertLevel.RED_ALERT)
                    {
                        addAntennaAlert(ALERT_VERY_LOW_STORAGE);
                    }
                    else if (i == (int)AlertLevel.WHITE_ALERT)
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
                config_options[CONFIGSTR_OP_MODE] = "base";
            }
            else if (isGenericShipMode())
            {
                config_options[CONFIGSTR_OP_MODE] = "ship";
            }
            else if (isDrillMode())
            {
                config_options[CONFIGSTR_OP_MODE] = "drill";
            }
            else if (isWelderMode())
            {
                config_options[CONFIGSTR_OP_MODE] = "welder";
            }
            else if (isGrinderMode())
            {
                config_options[CONFIGSTR_OP_MODE] = "grinder";
            }
            else if (isTugMode())
            {
                config_options[CONFIGSTR_OP_MODE] = "tug";
            }
            config_options[CONFIGSTR_HUD_NOTIFICATIONS] = Convert.ToString(hud_notifications);
            config_options[CONFIGSTR_POWER_WATERMARKS] = getWatermarkStr(power_low_watermark, power_high_watermark);
            if (isShipMode())
            {
                config_options[CONFIGSTR_PUSH_ORE] = Convert.ToString(push_ore_to_base);
                config_options[CONFIGSTR_PUSH_INGOTS] = Convert.ToString(push_ingots_to_base);
                config_options[CONFIGSTR_PUSH_COMPONENTS] = Convert.ToString(push_components_to_base);
                config_options[CONFIGSTR_PULL_ORE] = Convert.ToString(pull_ore_from_base);
                config_options[CONFIGSTR_PULL_INGOTS] = Convert.ToString(pull_ingots_from_base);
                config_options[CONFIGSTR_PULL_COMPONENTS] = Convert.ToString(pull_components_from_base);
                config_options[CONFIGSTR_REFUEL_OXYGEN] = Convert.ToString(refuel_oxygen);
                config_options[CONFIGSTR_REFUEL_HYDROGEN] = Convert.ToString(refuel_hydrogen);
            }
            config_options[CONFIGSTR_SORT_STORAGE] = Convert.ToString(sort_storage);
            if (throw_out_stone)
            {
                if (material_thresholds[STONE] == 0)
                {
                    config_options[CONFIGSTR_KEEP_STONE] = "none";
                }
                else
                {
                    config_options[CONFIGSTR_KEEP_STONE] = Convert.ToString(Math.Floor((material_thresholds[STONE] * 5) / 1000));
                }
            }
            else
            {
                config_options[CONFIGSTR_KEEP_STONE] = "all";
            }
            config_options[CONFIGSTR_OXYGEN_WATERMARKS] = String.Format("{0}", oxygen_high_watermark >= 0 ? getWatermarkStr(oxygen_low_watermark, oxygen_high_watermark) : "none");
            config_options[CONFIGSTR_HYDROGEN_WATERMARKS] = String.Format("{0}", hydrogen_high_watermark >= 0 ? getWatermarkStr(hydrogen_low_watermark, hydrogen_high_watermark) : "none");

            // currently selected operation mode
            sb.AppendLine("# Operation mode.");
            sb.AppendLine("# Can be auto, base, ship, tug, drill, welder or grinder.");
            var key = CONFIGSTR_OP_MODE;
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CONFIGSTR_HUD_NOTIFICATIONS;
            sb.AppendLine("# HUD notifications for blocks and antennas.");
            sb.AppendLine("# Can be True or False.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CONFIGSTR_POWER_WATERMARKS;
            sb.AppendLine("# Amount of power on batteries/reactors, in minutes.");
            sb.AppendLine("# Can be \"auto\", or two positive numbers.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refueling.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CONFIGSTR_OXYGEN_WATERMARKS;
            sb.AppendLine("# Percentage of oxygen to keep.");
            sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 0 and 100.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refining ice.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CONFIGSTR_HYDROGEN_WATERMARKS;
            sb.AppendLine("# Percentage of hydrogen to keep.");
            sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 0 and 100.");
            sb.AppendLine("# First number is when to sound an alarm.");
            sb.AppendLine("# Second number is when to stop refining ice.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            if (isShipMode())
            {
                key = CONFIGSTR_REFUEL_OXYGEN;
                sb.AppendLine("# Automatically refuel oxygen on connection.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_REFUEL_HYDROGEN;
                sb.AppendLine("# Automatically refuel hydrogen on connection.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
            }
            key = CONFIGSTR_KEEP_STONE;
            sb.AppendLine("# How much gravel to keep, in tons.");
            sb.AppendLine("# Can be a positive number, \"none\", \"all\" or \"auto\".");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();
            key = CONFIGSTR_SORT_STORAGE;
            sb.AppendLine("# Automatically sort items in storage containers.");
            sb.AppendLine("# Can be True or False.");
            sb.AppendLine(key + " = " + config_options[key]);
            sb.AppendLine();

            // these values only apply to ships
            if (isShipMode())
            {
                key = CONFIGSTR_PUSH_ORE;
                sb.AppendLine("# Push ore to base storage.");
                sb.AppendLine("# In tug mode, also pull ore from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_PUSH_INGOTS;
                sb.AppendLine("# Push ingots to base storage.");
                sb.AppendLine("# In tug mode, also pull ingots from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_PUSH_COMPONENTS;
                sb.AppendLine("# Push components to base storage.");
                sb.AppendLine("# In tug mode, also pull components from ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_PULL_ORE;
                sb.AppendLine("# Pull ore from base storage.");
                sb.AppendLine("# In tug mode, also push ore to ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_PULL_INGOTS;
                sb.AppendLine("# Pull ingots from base storage.");
                sb.AppendLine("# In tug mode, also push ingots to ships.");
                sb.AppendLine("# Can be True or False.");
                sb.AppendLine(key + " = " + config_options[key]);
                sb.AppendLine();
                key = CONFIGSTR_PULL_COMPONENTS;
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
            var strval = strs[1].ToLower().Trim();
            // backwards compatibility - goes one version back

            // v1.4x
            if (str == "reactor low watermark" || str == "power low watermark")
            {
                if (float.TryParse(strval, out power_low_watermark))
                {
                    return;
                }
                else
                {
                    throw new BarabasException("Invalid config value: " + strval, this);
                }
            }
            if (str == "reactor high watermark" || str == "power high watermark")
            {
                if (float.TryParse(strval, out power_high_watermark))
                {
                    return;
                }
                else
                {
                    throw new BarabasException("Invalid config value: " + strval, this);
                }
            }
            if (str == "oxygen threshold")
            {
                float tmp;
                if (strval == "none")
                {
                    oxygen_low_watermark = -1;
                    oxygen_high_watermark = -1;
                    return;
                }
                else if (float.TryParse(strval, out tmp) && tmp >= 0 && tmp <= 100)
                {
                    oxygen_low_watermark = tmp;
                    oxygen_high_watermark = (float)Math.Min(tmp * 2, 100);
                    return;
                }
                else
                {
                    throw new BarabasException("Invalid config value: " + strval, this);
                }
            }
            if (str == "hydrogen threshold")
            {
                float tmp;
                if (strval == "none")
                {
                    hydrogen_low_watermark = -1;
                    hydrogen_high_watermark = -1;
                    return;
                }
                else if (float.TryParse(strval, out tmp) && tmp >= 0 && tmp <= 100)
                {
                    hydrogen_low_watermark = tmp;
                    hydrogen_high_watermark = (float)Math.Min(tmp * 2, 100);
                    return;
                }
                else
                {
                    throw new BarabasException("Invalid config value: " + strval, this);
                }
            }
            if (!config_options.ContainsKey(str))
            {
                throw new BarabasException("Invalid config option: " + str, this);
            }
            // now, try to parse it
            bool fail = false;
            bool bval, bparse, fparse;
            float fval;
            bparse = Boolean.TryParse(strval, out bval);
            fparse = float.TryParse(strval, out fval);
            // op mode
            if (clStrCompare(str, CONFIGSTR_OP_MODE))
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
            else if (clStrCompare(str, CONFIGSTR_POWER_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    power_low_watermark = 0;
                    power_high_watermark = 0;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low > 0)
                    {
                        power_low_watermark = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high > 0 && low <= high)
                    {
                        power_high_watermark = high;
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
            else if (clStrCompare(str, CONFIGSTR_KEEP_STONE))
            {
                if (fparse && fval > 0)
                {
                    throw_out_stone = true;
                    material_thresholds[STONE] = (float)Math.Floor((fval * 1000) / 5);
                }
                else if (strval == "all")
                {
                    throw_out_stone = false;
                    material_thresholds[STONE] = 5000;
                }
                else if (strval == "none")
                {
                    throw_out_stone = true;
                    material_thresholds[STONE] = 0;
                }
                else if (strval == "auto")
                {
                    throw_out_stone = true;
                    material_thresholds[STONE] = 5000;
                }
                else
                {
                    fail = true;
                }
            }
            else if (clStrCompare(str, CONFIGSTR_OXYGEN_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    oxygen_low_watermark = 0;
                    oxygen_high_watermark = 0;
                }
                else if (strval == "none")
                {
                    oxygen_low_watermark = -1;
                    oxygen_high_watermark = -1;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low >= 0 && low <= 100)
                    {
                        oxygen_low_watermark = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high >= 0 && high <= 100 && low <= high)
                    {
                        oxygen_high_watermark = high;
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
            else if (clStrCompare(str, CONFIGSTR_HYDROGEN_WATERMARKS))
            {
                float low, high;
                if (strval == "auto")
                {
                    hydrogen_low_watermark = 0;
                    hydrogen_high_watermark = 0;
                }
                else if (strval == "none")
                {
                    hydrogen_low_watermark = -1;
                    hydrogen_high_watermark = -1;
                }
                else if (parseWatermarkStr(strval, out low, out high))
                {
                    if (low >= 0 && low <= 100)
                    {
                        hydrogen_low_watermark = low;
                    }
                    else
                    {
                        fail = true;
                    }
                    if (high >= 0 && high <= 100 && low <= high)
                    {
                        hydrogen_high_watermark = high;
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
            // bools
            else if (bparse)
            {
                if (isShipMode())
                {
                    if (clStrCompare(str, CONFIGSTR_PUSH_ORE))
                    {
                        push_ore_to_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_PUSH_INGOTS))
                    {
                        push_ingots_to_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_PUSH_COMPONENTS))
                    {
                        push_components_to_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_PULL_ORE))
                    {
                        pull_ore_from_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_PULL_INGOTS))
                    {
                        pull_ingots_from_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_PULL_COMPONENTS))
                    {
                        pull_components_from_base = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_REFUEL_OXYGEN))
                    {
                        refuel_oxygen = bval;
                    }
                    else if (clStrCompare(str, CONFIGSTR_REFUEL_HYDROGEN))
                    {
                        refuel_hydrogen = bval;
                    }
                }
                if (clStrCompare(str, CONFIGSTR_SORT_STORAGE))
                {
                    sort_storage = bval;
                }
                else if (clStrCompare(str, CONFIGSTR_HUD_NOTIFICATIONS))
                {
                    hud_notifications = bval;
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
            if (hud_notifications)
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

        void displayStatusReport()
        {
            var panels = getTextPanels();

            if (panels.Count == 0)
            {
                return;
            }

            removeAntennaAlert(ALERT_CRISIS_LOCKUP);
            removeAntennaAlert(ALERT_CRISIS_STANDBY);
            removeAntennaAlert(ALERT_CRISIS_THROW_OUT);

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
            foreach (IMyTextPanel panel in panels)
            {
                panel.WritePublicText(sb);
                panel.WritePublicTitle("BARABAS Notify Report");
                panel.ShowTextureOnScreen();
                panel.ShowPublicTextOnScreen();
            }
        }

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
            has_arc_furnaces = getArcFurnaces(true).Count > 0;
            can_refine = has_refineries || has_arc_furnaces || (getOxygenGenerators(true).Count > 0);
            can_refine_ice = has_refineries || (getOxygenGenerators().Count > 0);
            can_use_ingots = getAssemblers(true).Count > 0;
            getStorage(true);
            turnOffConveyors();
        }

        void s_refreshPower()
        {
            has_reactors = getReactors(true).Count > 0;
            getBatteries(true);
            if (has_reactors)
            {
                getMaxReactorPowerOutput(true);
                getCurReactorPowerOutput(true);
            }
            getMaxBatteryPowerOutput(true);
            getCurPowerDraw(true);
            getMaxPowerDraw(true);
        }

        void s_refreshOxyHydro()
        {
            has_air_vents = getAirVents(true).Count > 0;
            has_oxygen_tanks = getOxygenTanks(true).Count > 0;
            has_hydrogen_tanks = getHydrogenTanks(true).Count > 0;
            can_use_oxygen = has_oxygen_tanks && has_air_vents;
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

            // backwards compatibility: config block is deprecated
            var cb = getConfigBlock(true);
            if (cb != null)
            {
                // older version of BARABAS stored config in public text
                if (cb.GetPublicTitle() == "BARABAS Configuration")
                {
                    Me.CustomData = cb.GetPublicText();
                }
                else if (cb.GetPrivateTitle() == "BARABAS Configuration")
                {
                    Me.CustomData = cb.GetPrivateText();
                }
                cb.WritePrivateText("");
                cb.WritePrivateTitle("");
                cb.WritePublicText("DEPRECATED\n\nThis block is deprecated.\nPlease remove it.");
                cb.WritePublicTitle("DEPRECATED");
                cb.ShowPublicTextOnScreen();
                cb.CustomName = cb.CustomName + " [BARABAS: Deprecated]";
                cb.ShowOnHUD = true;
            }
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
                addAlert(AlertLevel.GREEN_ALERT);
            }
            else
            {
                removeAlert(AlertLevel.GREEN_ALERT);
            }
        }

        void s_power()
        {
            // determine if we need more uranium
            bool above_high_watermark = powerAboveHighWatermark();
            var max_pwr_output = getCurReactorPowerOutput() + getMaxBatteryPowerOutput();

            // if we have enough uranium ingots, business as usual
            if (!above_high_watermark)
            {
                // check if we're below low watermark
                bool above_low_watermark = powerAboveLowWatermark();

                if (has_reactors && has_refineries && ore_status[URANIUM] > 0)
                {
                    prioritize_uranium = true;
                }

                if (!above_low_watermark && max_pwr_output != 0)
                {
                    addAlert(AlertLevel.BLUE_ALERT);
                }
                else
                {
                    removeAlert(AlertLevel.BLUE_ALERT);
                }
            }
            else
            {
                removeAlert(AlertLevel.BLUE_ALERT);
                prioritize_uranium = false;
            }
            status_report[STATUS_POWER_STATS] = "No power sources";

            if (max_pwr_output != 0)
            {
                // figure out how much time we have on batteries and reactors
                float stored_power = getBatteryStoredPower() + getReactorStoredPower();
                // prevent division by zero
                var max_pwr_draw = (float)Math.Max(getMaxPowerDraw(), 0.001F);
                var cur_pwr_draw = (float)Math.Max(getCurPowerDraw(), 0.001F);
                var adjusted_pwr_draw = (cur_pwr_draw + prev_pwr_draw) / 2;
                prev_pwr_draw = cur_pwr_draw;
                float time;
                string time_str;
                if (isShipMode() && connected_to_base)
                {
                    time = (float)Math.Round(stored_power / max_pwr_draw, 0);
                }
                else
                {
                    time = (float)Math.Round(stored_power / adjusted_pwr_draw, 0);
                }
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
                            time_str = Convert.ToString(time) + " d";
                        }
                    }
                    else
                    {
                        time_str = Convert.ToString(time) + " h";
                    }
                }
                else
                {
                    time_str = Convert.ToString(time) + " m";
                }
                string max_str = String.Format("{0:0.0}%", max_pwr_draw / max_pwr_output * 100);
                string cur_str = String.Format("{0:0.0}%", adjusted_pwr_draw / max_pwr_output * 100);
                status_report[STATUS_POWER_STATS] = String.Format("{0}/{1}/{2}", max_str, cur_str, time_str);
            }

            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE)
            {
                bool can_refuel = isShipMode() && connected_to_base;
                if (refillReactors())
                {
                    pushSpareUraniumToStorage();
                }
            }
            // if we're in a crisis, push all available uranium ingots to reactors.
            else
            {
                refillReactors(true);
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
            if (crisis_mode == CrisisMode.CRISIS_MODE_NONE && throw_out_stone)
            {
                // check if we want to throw out extra stone
                if (storage_ore_status[STONE] > 0 || storage_ingot_status[STONE] > 0)
                {
                    var excessStone = storage_ingot_status[STONE] + storage_ore_status[STONE] * ore_to_ingot_ratios[STONE] - material_thresholds[STONE] * 5;
                    excessStone = (float)Math.Min(excessStone / ore_to_ingot_ratios[STONE], storage_ore_status[STONE]);
                    var excessGravel = storage_ingot_status[STONE] - material_thresholds[STONE] * 5;
                    if (excessStone > 0)
                    {
                        throwOutOre(STONE, excessStone);
                    }
                    else if (excessGravel > 0)
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
                string ore = getBiggestOre();
                if ((ore != null && !throwOutOre(ore, 0, true)) || ore == null)
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
            var blocks = getBlocks();
            foreach (var b in blocks)
            {
                for (int j = 0; j < b.InventoryCount; j++)
                {
                    var inv = b.GetInventory(j);
                    var items = inv.GetItems();
                    foreach (var i in items)
                    {
                        bool isStorage = b is IMyCargoContainer;
                        bool isReactor = b is IMyReactor;
                        if (isOre(i))
                        {
                            string name = i.Content.SubtypeName;
                            if (i.Content.SubtypeName == SCRAP)
                            {
                                name = IRON;
                            }
                            ore_status[name] += (float)i.Amount;
                            if (isStorage)
                            {
                                storage_ore_status[name] += (float)i.Amount;
                            }
                        }
                        else if (isIngot(i))
                        {
                            string name = i.Content.SubtypeName;
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
            }
            bool alert = false;
            var sb = new StringBuilder();
            foreach (var ore in ore_types)
            {
                float total_ingots = 0;
                float total_ore = ore_status[ore];
                float total = 0;
                if (ore != ICE)
                {
                    total_ingots = ingot_status[ore];
                    if (ore == URANIUM)
                    {
                        total_ingots -= uranium_in_reactors;
                    }
                    total = (total_ore * ore_to_ingot_ratios[ore]) + total_ingots;
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
                    sb.Append(ore);
                    sb.Append(": ");
                    sb.Append(roundStr(total_ore));

                    if (ore != ICE)
                    {
                        sb.Append(" / ");
                        sb.Append(roundStr(total_ingots));
                        if (total_ore > 0)
                        {
                            sb.Append(String.Format(" ({0})", roundStr(total)));
                        }
                    }
                }

                if (ore != ICE && can_use_ingots && total < material_thresholds[ore])
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
                addAlert(AlertLevel.WHITE_ALERT);
            }
            else
            {
                removeAlert(AlertLevel.WHITE_ALERT);
            }
            alert = false;
            status_report[STATUS_MATERIAL] = sb.ToString();

            // display oxygen and hydrogen stats
            if (has_oxygen_tanks || has_hydrogen_tanks)
            {
                float oxy_cur = 0, oxy_total = 0;
                float hydro_cur = 0, hydro_total = 0;
                var tanks = getOxygenTanks();
                foreach (IMyGasTank tank in tanks)
                {
                    oxy_cur += tank.FilledRatio;
                    oxy_total += 1;
                }
                tanks = getHydrogenTanks();
                foreach (IMyGasTank tank in tanks)
                {
                    hydro_cur += tank.FilledRatio;
                    hydro_total += 1;
                }
                cur_oxygen_level = has_oxygen_tanks ? (oxy_cur / oxy_total) * 100 : 0;
                cur_hydrogen_level = has_hydrogen_tanks ? (hydro_cur / hydro_total) * 100 : 0;
                string oxy_str = !has_oxygen_tanks ? "N/A" : String.Format("{0:0.0}%",
                    cur_oxygen_level);
                string hydro_str = !has_hydrogen_tanks ? "N/A" : String.Format("{0:0.0}%",
                    cur_hydrogen_level);

                if (has_oxygen_tanks && oxygen_low_watermark > 0 && cur_oxygen_level + getStoredOxygen() < oxygen_low_watermark)
                {
                    alert = true;
                    addAntennaAlert(ALERT_LOW_OXYGEN);
                }
                else
                {
                    removeAntennaAlert(ALERT_LOW_OXYGEN);
                }
                if (has_hydrogen_tanks && hydrogen_low_watermark > 0 && cur_hydrogen_level + getStoredHydrogen() < hydrogen_low_watermark)
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
                    status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1} WARNING!",
                        oxy_str, hydro_str);
                    addAlert(AlertLevel.CYAN_ALERT);
                }
                else
                {
                    status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1}",
                        oxy_str, hydro_str);
                    removeAlert(AlertLevel.CYAN_ALERT);
                }
            }
            else
            {
                removeAntennaAlert(ALERT_LOW_OXYGEN);
                removeAntennaAlert(ALERT_LOW_HYDROGEN);
                status_report[STATUS_OXYHYDRO_LEVEL] = "";
                removeAlert(AlertLevel.CYAN_ALERT);
            }
        }

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
            var last_cycle_count = state_cycle_counts[next_state];

            // estimate cycle count after executing the next state
            int projected_cycle_count = cur_i + last_cycle_count;

            // given our estimate, how are we doing with regards to IL count limits?
            float cycle_p = projected_cycle_count / Runtime.MaxInstructionCount;

            // if we never executed the next state, we leave 60% headroom for our next
            // state (for all we know it could be a big state), otherwise leave at 20%
            // because we already know how much it usually takes and it's unlikely to
            // suddenly become much bigger than what we've seen before
            var cycle_thresh = canEstimate ? 0.8F : 0.4F;

            // check if we are exceeding our stated thresholds (projected 80% cycle
            // count for known states, or 40% up to this point for unknown states)
            bool haveEnoughHeadroom = cycle_p <= cycle_thresh;

            // advance current state and store IL count values
            current_state = next_state;
            cur_cycle_count = cur_i;

            return haveEnoughHeadroom;
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

        public void Save()
        {
            var sb = new StringBuilder();
            sb.Append(tried_throwing.ToString());
            Storage = sb.ToString();
        }

        // constructor
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

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
            var bd = Me.BlockDefinition.ToString();
            large_grid = bd.Contains("Large");

            Me.ShowOnHUD = false;

            if (Storage.Length > 0)
            {
                bool invalid = false;
                string[] strs = Storage.Split(':');

                string throw_str = null;

                // backwards compatibility with 1.5x: it had a config block
                switch (strs.Length)
                {
                    case 1:
                        throw_str = strs[0];
                        break;
                    case 2:
                        throw_str = strs[1];
                        string id_str = strs[0];
                        long id;

                        if (long.TryParse(id_str, out id))
                        {
                            if (id != 0)
                            {
                                config_block = GridTerminalSystem.GetBlockWithId(id) as IMyTextPanel;
                            }
                        }
                        else
                        {
                            invalid = true;
                        }
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
            if (!timer_mode && pause_idx != 0)
            {
                pause_idx = (pause_idx + 1) % 6;
                return;
            }
            int num_states = 0;

            // if we're activated by a timer, go into timer mode and do not ever
            // update the UpdateFrequency
            if (ut == UpdateType.Trigger)
            {
                timer_mode = true;
                Runtime.UpdateFrequency = UpdateFrequency.None;
            } else
            {
                timer_mode = false;
                Runtime.UpdateFrequency = UpdateFrequency.Update10;
            }

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

            if (refineries_clogged || arc_furnaces_clogged || assemblers_clogged)
            {
                addAlert(AlertLevel.MAGENTA_ALERT);
            }
            else
            {
                removeAlert(AlertLevel.MAGENTA_ALERT);
            }

            // display status updates
            if (has_status_panels)
            {
                displayStatusReport();
            }
            string il_str = String.Format("IL Count: {0}/{1} ({2:0.0}%)",
                Runtime.CurrentInstructionCount,
                Runtime.MaxInstructionCount,
                (float)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount * 100F);
            Echo(String.Format("BARABAS version {0}", VERSION));
            Echo(String.Format("States executed: {0}", num_states));
            Echo(il_str);
        }
        #region SEFOOTER
#if DEBUG
    }
}
#endif
#endregion
