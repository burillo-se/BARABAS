/*
 * BARABAS v1.5beta2
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
 * To configure BARABAS, edit configuration in a text block called "BARABAS Config".
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
 * - Text block named "BARABAS Config", used for storing configuration (if not
 *   present, automatic configuration will be used)
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

// configuration
const int OP_MODE_AUTO = 0x0;
const int OP_MODE_SHIP = 0x1;
const int OP_MODE_DRILL = 0x2;
const int OP_MODE_GRINDER = 0x4;
const int OP_MODE_WELDER = 0x8;
const int OP_MODE_TUG = 0x10;
const int OP_MODE_BASE = 0x100;

int op_mode = OP_MODE_AUTO;
Decimal power_high_watermark = 0M;
Decimal power_low_watermark = 0M;
Decimal oxygen_high_watermark = 0M;
Decimal oxygen_low_watermark = 0M;
Decimal hydrogen_high_watermark = 0M;
Decimal hydrogen_low_watermark = 0M;
bool throw_out_stone = false;
bool sort_storage = true;
bool hud_notifications = true;
Decimal prev_pwr_draw = 0M;
bool refineries_clogged = false;
bool arc_furnaces_clogged = false;
bool assemblers_clogged = false;

bool push_ore_to_base = false;
bool push_ingots_to_base = false;
bool push_components_to_base = false;
bool pull_ore_from_base = false;
bool pull_ingots_from_base = false;
bool pull_components_from_base = false;

// state variables
int current_state;
int crisis_mode;

// crisis mode levels
const int CRISIS_MODE_NONE = 0;
const int CRISIS_MODE_THROW_ORE = 1;
const int CRISIS_MODE_LOCKUP = 2;

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

// ore_volume
const Decimal VOLUME_ORE = 0.37M;
const Decimal VOLUME_SCRAP = 0.254M;

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

const Decimal CHUNK_SIZE = 1000M;

// config options, caseless dictionary
readonly Dictionary < string, string > config_options = new Dictionary < string, string > (StringComparer.OrdinalIgnoreCase) {
  { CONFIGSTR_OP_MODE, "" },
  { CONFIGSTR_HUD_NOTIFICATIONS, "" },
  { CONFIGSTR_POWER_WATERMARKS, "" },
  { CONFIGSTR_OXYGEN_WATERMARKS, "" },
  { CONFIGSTR_HYDROGEN_WATERMARKS, "" },
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
readonly Dictionary < string, string > status_report = new Dictionary < string, string > {
  { STATUS_STORAGE_LOAD, "" },
  { STATUS_POWER_STATS, "" },
  { STATUS_OXYHYDRO_LEVEL, "" },
  { STATUS_MATERIAL, "" },
  { STATUS_ALERT, "" },
  { STATUS_CRISIS_MODE, "" },
};

readonly List < string > ore_types = new List < string > {
 COBALT, GOLD, IRON, MAGNESIUM, NICKEL, PLATINUM, SILICON, SILVER, URANIUM, STONE, ICE
};

readonly List < string > arc_furnace_ores = new List < string > {
 COBALT, IRON, NICKEL
};

// ballpark values of "just enough" for each material
readonly Dictionary < string, Decimal > material_thresholds = new Dictionary < string, Decimal > {
  { COBALT, 500M },
  { GOLD, 100M },
  { IRON, 5000M },
  { MAGNESIUM, 100M },
  { NICKEL, 1000M },
  { PLATINUM, 10M },
  { SILICON, 1000M },
  { SILVER, 1000M },
  { URANIUM, 10M },
  { STONE, 5000M },
};

readonly Dictionary < string, Decimal > ore_to_ingot_ratios = new Dictionary < string, Decimal > {
  { COBALT, 0.24M },
  { GOLD, 0.008M },
  { IRON, 0.56M },
  { MAGNESIUM, 0.0056M },
  { NICKEL, 0.32M },
  { PLATINUM, 0.004M },
  { SILICON, 0.56M },
  { SILVER, 0.08M },
  { URANIUM, 0.0056M },
  { STONE, 0.72M }
};

// statuses for ore and ingots
static readonly Dictionary < string, Decimal > ore_status = new Dictionary < string, Decimal > {
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
readonly Dictionary < string, Decimal > ingot_status = new Dictionary < string, Decimal > (ore_status);
readonly Dictionary < string, Decimal > storage_ore_status = new Dictionary < string, Decimal > (ore_status);
readonly Dictionary < string, Decimal > storage_ingot_status = new Dictionary < string, Decimal > (ore_status);

/* local data storage, updated once every few cycles */
List < IMyTerminalBlock > local_blocks = null;
List < IMyTerminalBlock > local_reactors = null;
List < IMyTerminalBlock > local_batteries = null;
List < IMyTerminalBlock > local_refineries = null;
List < IMyTerminalBlock > local_refineries_subset = null;
List < IMyTerminalBlock > local_arc_furnaces = null;
List < IMyTerminalBlock > local_arc_furnaces_subset = null;
List < IMyTerminalBlock > local_all_refineries = null;
List < IMyTerminalBlock > local_assemblers = null;
List < IMyTerminalBlock > local_connectors = null;
List < IMyTerminalBlock > local_storage = null;
List < IMyTerminalBlock > local_lights = null;
List < IMyTerminalBlock > local_drills = null;
List < IMyTerminalBlock > local_grinders = null;
List < IMyTerminalBlock > local_welders = null;
List < IMyTerminalBlock > local_text_panels = null;
List < IMyTerminalBlock > local_air_vents = null;
List < IMyTerminalBlock > local_oxygen_tanks = null;
List < IMyTerminalBlock > local_hydrogen_tanks = null;
List < IMyTerminalBlock > local_oxygen_generators = null;
List < IMyTerminalBlock > local_trash_connectors = null;
List < IMyTerminalBlock > local_trash_sensors = null;
List < IMyTerminalBlock > local_antennas = null;
List < IMyTerminalBlock > remote_storage = null;
List < IMyTerminalBlock > remote_ship_storage = null;
IMyTextPanel config_block = null;
List < IMyCubeGrid > local_grids = null;
List < IMyCubeGrid > remote_base_grids = null;
List < IMyCubeGrid > remote_ship_grids = null;
Dictionary < IMyCubeGrid, GridData > remote_grid_data = null;
GridData local_grid_data = null;

// alert levels, in priority order
const int RED_ALERT = 0;
const int YELLOW_ALERT = 1;
const int BLUE_ALERT = 2;
const int CYAN_ALERT = 3;
const int MAGENTA_ALERT = 4;
const int WHITE_ALERT = 5;
const int PINK_ALERT = 6;
const int BROWN_ALERT = 7;
const int GREEN_ALERT = 8;

public class Alert {
 public Alert(Color c, string t) {
  color = c;
  enabled = false;
  text = t;
 }
 public Color color;
 public bool enabled;
 public string text;
}

readonly List < Alert > text_alerts = new List < Alert > {
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

Dictionary < IMyTerminalBlock, int > blocks_to_alerts = new Dictionary < IMyTerminalBlock, int > ();

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

readonly Dictionary < int, string > block_alerts = new Dictionary < int, string > {
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
  { ALERT_CRISIS_STANDBY, "Crisis: standing by" }
};

/* misc local data */
bool power_above_threshold = false;
Decimal cur_power_draw;
Decimal max_power_draw;
Decimal max_battery_output;
Decimal max_reactor_output;
Decimal cur_reactor_output;
Decimal cur_oxygen_level;
Decimal cur_hydrogen_level;
bool tried_throwing = false;
bool auto_refuel_ship;
bool prioritize_uranium = false;
bool refine_ice = true;
bool can_use_ingots;
bool can_use_oxygen;
bool can_refine;
bool can_refine_ice;
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
Dictionary < string, Decimal > thrust_power = new Dictionary < string, Decimal > () {
  { "MyObjectBuilder_Thrust/SmallBlockSmallThrust", 33.6M },
  { "MyObjectBuilder_Thrust/SmallBlockLargeThrust", 400M },
  { "MyObjectBuilder_Thrust/LargeBlockSmallThrust", 560M },
  { "MyObjectBuilder_Thrust/LargeBlockLargeThrust", 6720M },
  { "MyObjectBuilder_Thrust/SmallBlockSmallHydrogenThrust", 0M },
  { "MyObjectBuilder_Thrust/SmallBlockLargeHydrogenThrust", 0M },
  { "MyObjectBuilder_Thrust/LargeBlockSmallHydrogenThrust", 0M },
  { "MyObjectBuilder_Thrust/LargeBlockLargeHydrogenThrust", 0M },
  { "MyObjectBuilder_Thrust/SmallBlockSmallAtmosphericThrust", 700M },
  { "MyObjectBuilder_Thrust/SmallBlockLargeAtmosphericThrust", 2400M },
  { "MyObjectBuilder_Thrust/LargeBlockSmallAtmosphericThrust", 2360M },
  { "MyObjectBuilder_Thrust/LargeBlockLargeAtmosphericThrust", 16360M }
};

// power constants - in kWatts
const Decimal URANIUM_INGOT_POWER = 68760M;

public class ItemHelper {
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
public class GridData {
 public bool has_thrusters;
 public bool has_wheels;
 public bool has_welders;
 public bool has_grinders;
 public bool has_drills;
 public bool override_ship;
 public bool override_base;
}

// grid graph edge class, represents a connection point between two grids, noting
// if this connection is via a connector (i.e. if it's an external grid connection).
public class GridGraphEdge {
 public GridGraphEdge(IMyCubeGrid src_in, IMyCubeGrid dst_in, bool connector_in) {
  src = src_in;
  dst = dst_in;
  is_connector = connector_in;
 }
 public IMyCubeGrid src;
 public IMyCubeGrid dst;
 public bool is_connector;
}

// comparer for graph edges - the way the comparison is done means the edges are
// bidirectional - meaning, it doesn't matter which grid is source and which
// grid is destination, they will be equal as far as comparison is concerned.
public class GridGraphEdgeComparer: IEqualityComparer < GridGraphEdge > {
 public int GetHashCode(GridGraphEdge e) {
  int hash = 13;
  // multiply src hashcode by dst hashcode - multiplication is commutative, so
  // result will be the same no matter which grid was source or destination
  hash = (hash * 7) + (e.src.GetHashCode() * e.dst.GetHashCode());
  hash = (hash * 7) + e.is_connector.GetHashCode();
  return hash;
 }
 public bool Equals(GridGraphEdge e1, GridGraphEdge e2) {
  if (e1.is_connector != e2.is_connector) {
   return false;
  }
  if (e1.src == e2.src && e1.dst == e2.dst) {
   return true;
  }
  if (e1.src == e2.dst && e1.dst == e2.src) {
   return true;
  }
  return false;
 }
}

// our grid graph
public class GridGraph {
 public GridGraph() {
  edges = new HashSet < GridGraphEdge > (new GridGraphEdgeComparer());
 }
  // add an edge to the graph
 public bool addEdge(IMyCubeGrid src, IMyCubeGrid dst, bool is_connector) {
  var t = new GridGraphEdge(src, dst, is_connector);
  var alt = new GridGraphEdge(src, dst, !is_connector);

  bool has_t = edges.Contains(t);
  bool has_alt = edges.Contains(alt);

  // avoid adding the same edge twice
  if (!has_t && !has_alt) {
   edges.Add(t);
   return true;
  } else if (has_alt && !is_connector) {
   // also, if we have a connector edge and a non-connector edge, non-connector
   // edge always wins
   edges.Remove(alt);
   edges.Add(t);
  }
  return false;
 }

 // get all grids that are local to source grid (i.e. all grids connected by
 // rotors or pistons)
 public List < IMyCubeGrid > getLocalGrids(IMyCubeGrid src) {
  var grids = new List < IMyCubeGrid > ();
  var seen = new HashSet < IMyCubeGrid > ();
  grids.Add(src);
  seen.Add(src);
  foreach (var edge in edges) {
   // local grid is a grid which is not connected by a connector
   if (!edge.is_connector && seen.Contains(edge.src) && !seen.Contains(edge.dst)) {
    grids.Add(edge.dst);
    seen.Add(edge.dst);
   }
   if (!edge.is_connector && seen.Contains(edge.dst) && !seen.Contains(edge.src)) {
    grids.Add(edge.src);
    seen.Add(edge.src);
   }
  }
  return grids;
 }

 // get all neighboring (connected by connectors) grid entry points
 public List < GridGraphEdge > getGridConnections() {
  var list = new List < GridGraphEdge > ();
  foreach (var edge in edges) {
   if (!edge.is_connector) {
    continue;
   }
   list.Add(edge);
  }
  return list;
 }
 HashSet < GridGraphEdge > edges; // HashSet for constant time access
}

// just have a method to indicate that this exception comes from BARABAS
class BarabasException: Exception {
 public BarabasException(string msg, Program p) : base("BARABAS: " + msg) {
  // calling the getX() functions will set off a chain of events if local data
  // is not initialized, so use locally stored data instead
  var panels = p.local_text_panels;
  if (panels != null && panels.Count > 0) {
   foreach (IMyTextPanel panel in panels) {
    panel.WritePublicText(" BARABAS EXCEPTION:\n" + msg);
    panel.ShowTextureOnScreen();
    panel.ShowPublicTextOnScreen();
   }
  }
  p.Me.SetCustomName("BARABAS Exception: " + msg);
  p.showOnHud(p.Me);
  if (p.local_lights != null) {
   p.showAlertColor(Color.Red);
  }
 }
}

/**
 * Filters
 */
bool excludeBlock(IMyTerminalBlock block) {
 if (block.CustomName.StartsWith("X")) {
  return true;
 }
 if (!block.IsFunctional) {
  return true;
 }
 return false;
}

bool localGridFilter(IMyTerminalBlock block) {
 if (excludeBlock(block)) {
  return false;
 }
 return getLocalGrids().Contains(block.CubeGrid);
}

bool remoteGridFilter(IMyTerminalBlock block) {
 if (excludeBlock(block)) {
  return false;
 }
 return getRemoteGrids().Contains(block.CubeGrid);
}

// this filter only gets remote ships - used for tug mode
bool shipFilter(IMyTerminalBlock block) {
 if (excludeBlock(block)) {
  return false;
 }
 return getShipGrids().Contains(block.CubeGrid);
}

/**
 * Grid and block functions
 */
// filter blocks by type and locality
public void filterLocalGrid < T > (List < IMyTerminalBlock > blocks) {
 var grids = getLocalGrids();
 for (int i = blocks.Count - 1; i >= 0; i--) {
  var block = blocks[i];
  var grid = block.CubeGrid;
  if (!(block is T) || !grids.Contains(grid)) {
   blocks.RemoveAt(i);
  }
 }
}

// remove null (destroyed) blocks from list
HashSet < List < IMyTerminalBlock >> null_list;
List < IMyTerminalBlock > removeNulls(List < IMyTerminalBlock > list) {
 if (null_list.Contains(list)) {
  return list;
 }
 null_list.Add(list);
 for (int i = list.Count - 1; i >= 0; i--) {
  var block = list[i];
  if (!blockExists(block)) {
   blocks_to_alerts.Remove(block);
   list.RemoveAt(i);
  }
 }
 return list;
}

IMySlimBlock slimBlock(IMyTerminalBlock block) {
 return block.CubeGrid.GetCubeBlock(block.Position);
}

bool blockExists(IMyTerminalBlock block) {
 return block.CubeGrid.CubeExists(block.Position);
}

// does what it says on the tin: picks random subset of a list
List < IMyTerminalBlock > randomSubset(List < IMyTerminalBlock > list, int limit) {
 int len = Math.Min(list.Count, limit);
 var random_idx = new List < int > ();
 var result = new List < IMyTerminalBlock > ();

 // initialize the index list
 for (int i = 0; i < list.Count; i++) {
  random_idx.Add(i);
 }

 // randomize the list
 Random rng = new Random();
 for (int i = 0; i < random_idx.Count; i++) {
  random_idx.Swap(i, rng.Next(0, random_idx.Count));
 }

 // now, pick out the random subset
 for (int i = 0; i < len; i++) {
  result.Add(list[random_idx[i]]);
 }

 return result;
}

// get all local blocks
List < IMyTerminalBlock > getBlocks(bool force_update = false) {
 if (local_blocks != null && !force_update) {
  return removeNulls(local_blocks);
 }
 filterLocalGrid < IMyTerminalBlock > (local_blocks);

 bool alert = false;
 // check if we have unfinished blocks
 for (int i = local_blocks.Count - 1; i >= 0; i--) {
  var block = local_blocks[i];
  if (!slimBlock(block).IsFullIntegrity) {
   alert = true;
   addBlockAlert(block, ALERT_DAMAGED);
  } else {
   removeBlockAlert(block, ALERT_DAMAGED);
  }
  displayBlockAlerts(block);
 }
 if (alert) {
  addAlert(PINK_ALERT);
 } else {
  removeAlert(PINK_ALERT);
 }
 return local_blocks;
}

List < IMyTerminalBlock > getReactors(bool force_update = false) {
 if (local_reactors != null && !force_update) {
  return removeNulls(local_reactors);
 }
 filterLocalGrid < IMyReactor > (local_reactors);
 foreach (var reactor in local_reactors) {
  var inv = reactor.GetInventory(0);
  if (inv.GetItems().Count > 1) {
   consolidate(inv);
  }
 }
 return local_reactors;
}

List < IMyTerminalBlock > getBatteries(bool force_update = false) {
 if (local_batteries != null && !force_update) {
  return removeNulls(local_batteries);
 }
 filterLocalGrid < IMyBatteryBlock > (local_batteries);
 for (int i = local_batteries.Count - 1; i >= 0; i--) {
  if ((local_batteries[i] as IMyBatteryBlock).OnlyRecharge) {
   local_batteries.RemoveAt(i);
  }
 }
 return local_batteries;
}

List < IMyTerminalBlock > getStorage(bool force_update = false) {
 if (local_storage != null && !force_update) {
  return removeNulls(local_storage);
 }
 filterLocalGrid < IMyCargoContainer > (local_storage);
 foreach (var storage in local_storage) {
  var inv = storage.GetInventory(0);
  consolidate(inv);
 }
 return local_storage;
}

List < IMyTerminalBlock > getRefineries(bool force_update = false) {
 if (local_refineries != null && !force_update) {
  // if we didn't refresh the list yet, get a random subset
  if (!null_list.Contains(local_refineries_subset)) {
   local_refineries_subset = randomSubset(local_refineries, 50);
  }
  return removeNulls(local_refineries_subset);
 }
 refineries_clogged = false;
 filterLocalGrid < IMyRefinery > (local_refineries);
 foreach (IMyRefinery refinery in local_refineries) {
  var input_inv = refinery.GetInventory(0);
  var output_inv = refinery.GetInventory(1);
  Decimal input_load = (Decimal) input_inv.CurrentVolume / (Decimal) input_inv.MaxVolume;
  Decimal output_load = (Decimal) output_inv.CurrentVolume / (Decimal) output_inv.MaxVolume;
  if (!refinery.IsQueueEmpty && !refinery.IsProducing) {
   addBlockAlert(refinery, ALERT_CLOGGED);
   refineries_clogged = true;
  } else {
   removeBlockAlert(refinery, ALERT_CLOGGED);
  }
  displayBlockAlerts(refinery);
 }
 if (!null_list.Contains(local_refineries_subset)) {
  local_refineries_subset = randomSubset(local_refineries, 50);
 }
 return local_refineries_subset;
}

List < IMyTerminalBlock > getArcFurnaces(bool force_update = false) {
 if (local_arc_furnaces != null && !force_update) {
  // if we didn't refresh the list yet, get a random subset
  if (!null_list.Contains(local_arc_furnaces_subset)) {
   local_arc_furnaces_subset = randomSubset(local_arc_furnaces, 30);
  }
  return removeNulls(local_arc_furnaces_subset);
 }
 arc_furnaces_clogged = false;
 filterLocalGrid < IMyRefinery > (local_arc_furnaces);
 foreach (IMyRefinery furnace in local_arc_furnaces) {
  var input_inv = furnace.GetInventory(0);
  var output_inv = furnace.GetInventory(1);
  Decimal input_load = (Decimal) input_inv.CurrentVolume / (Decimal) input_inv.MaxVolume;
  Decimal output_load = (Decimal) output_inv.CurrentVolume / (Decimal) output_inv.MaxVolume;
  if (!furnace.IsQueueEmpty && !furnace.IsProducing) {
   addBlockAlert(furnace, ALERT_CLOGGED);
   arc_furnaces_clogged = true;
  } else {
   removeBlockAlert(furnace, ALERT_CLOGGED);
  }
  displayBlockAlerts(furnace);
 }
 if (!null_list.Contains(local_arc_furnaces_subset)) {
  local_arc_furnaces_subset = randomSubset(local_arc_furnaces, 30);
 }
 return local_arc_furnaces_subset;
}

List < IMyTerminalBlock > getAllRefineries() {
 if (local_all_refineries == null) {
  local_all_refineries = new List < IMyTerminalBlock > ();
 }
 if (!null_list.Contains(local_all_refineries)) {
  local_all_refineries.Clear();
  local_all_refineries.AddRange(getRefineries());
  local_all_refineries.AddRange(getArcFurnaces());
 }
 return removeNulls(local_all_refineries);
}

List < IMyTerminalBlock > getAssemblers(bool force_update = false) {
 if (local_assemblers != null && !force_update) {
  return removeNulls(local_assemblers);
 }
 assemblers_clogged = false;
 filterLocalGrid < IMyAssembler > (local_assemblers);
 for (int i = local_assemblers.Count - 1; i >= 0; i--) {
  var block = local_assemblers[i] as IMyAssembler;
  if (block.DisassembleEnabled) {
   local_assemblers.RemoveAt(i);
  } else {
   consolidate(block.GetInventory(0));
   consolidate(block.GetInventory(1));
   var input_inv = block.GetInventory(0);
   var output_inv = block.GetInventory(1);
   Decimal input_load = (Decimal) input_inv.CurrentVolume / (Decimal) input_inv.MaxVolume;
   Decimal output_load = (Decimal) output_inv.CurrentVolume / (Decimal) output_inv.MaxVolume;
   bool isWaiting = !block.IsQueueEmpty && !block.IsProducing;
   removeBlockAlert(block, ALERT_MATERIALS_MISSING);
   removeBlockAlert(block, ALERT_CLOGGED);
   if ((input_load > 0.98M || output_load > 0.98M) && isWaiting) {
    addBlockAlert(block, ALERT_CLOGGED);
    assemblers_clogged = true;
   } else if (isWaiting) {
    addBlockAlert(block, ALERT_MATERIALS_MISSING);
    assemblers_clogged = true;
   }
   displayBlockAlerts(block);
  }
 }
 return local_assemblers;
}

List < IMyTerminalBlock > getConnectors(bool force_update = false) {
 if (local_connectors != null && !force_update) {
  return removeNulls(local_connectors);
 }
 filterLocalGrid < IMyShipConnector > (local_connectors);
 foreach (IMyShipConnector connector in local_connectors) {
  consolidate(connector.GetInventory(0));
  // prepare the connector
  // at this point, we have already turned off the conveyors,
  // so now just check if we aren't throwing anything already and
  // if we aren't in "collect all" mode
  if (connector.CollectAll) {
   // disable collect all
   connector.ApplyAction("CollectAll");
  }
 }
 return local_connectors;
}

// get notification lights
List < IMyTerminalBlock > getLights(bool force_update = false) {
 if (local_lights != null && !force_update) {
  return removeNulls(local_lights);
 }
 // find our group
 local_lights = new List < IMyTerminalBlock > ();
 var group = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");
 if (group != null) {
  group.GetBlocks(local_lights, localGridFilter);
 }
 filterLocalGrid < IMyLightingBlock > (local_lights);
 return local_lights;
}

// get status report text panels
List < IMyTerminalBlock > getTextPanels(bool force_update = false) {
 if (local_text_panels != null && !force_update) {
  return removeNulls(local_text_panels);
 }
 // find our group
 local_text_panels = new List < IMyTerminalBlock > ();

 var group = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");

 if (group != null) {
  group.GetBlocks(local_text_panels, localGridFilter);
 }

 // we may find multiple Status groups, as we may have a BARABAS-driven
 // ships connected, so let's filter text panels
 filterLocalGrid < IMyTextPanel > (local_text_panels);

 // if the user accidentally included a config block into this group,
 // notify him immediately
 if (local_text_panels.Contains(getConfigBlock() as IMyTerminalBlock)) {
  throw new BarabasException("Configuration text panel should not " +
   "be part of BARABAS Notify group", this);
 }
 return local_text_panels;
}

// get status report text panels
List < IMyTerminalBlock > getAntennas(bool force_update = false) {
 if (local_text_panels != null && !force_update) {
  return removeNulls(local_antennas);
 }
 // find our group
 local_antennas = new List < IMyTerminalBlock > ();
 var group = GridTerminalSystem.GetBlockGroupWithName("BARABAS Notify");

 if (group != null) {
  var tmp_antennas = new List < IMyTerminalBlock > ();
  var tmp_beacons = new List < IMyTerminalBlock > ();
  var tmp_laser = new List < IMyTerminalBlock > ();
  group.GetBlocks(tmp_antennas);
  group.GetBlocks(tmp_beacons);
  group.GetBlocks(tmp_laser);

  // we may find multiple Status groups, as we may have a BARABAS-driven
  // ships connected, so let's filter text panels
  filterLocalGrid < IMyBeacon > (tmp_beacons);
  filterLocalGrid < IMyRadioAntenna > (tmp_antennas);
  filterLocalGrid < IMyLaserAntenna > (tmp_laser);

  // populate the list
  local_antennas.AddRange(tmp_beacons);
  local_antennas.AddRange(tmp_antennas);
  local_antennas.AddRange(tmp_laser);
 }

 return local_antennas;
}

List < IMyTerminalBlock > getDrills(bool force_update = false) {
 if (local_drills != null && !force_update) {
  return removeNulls(local_drills);
 }
 filterLocalGrid < IMyShipDrill > (local_drills);
 foreach (var drill in local_drills) {
  consolidate(drill.GetInventory(0));
 }
 return local_drills;
}

List < IMyTerminalBlock > getGrinders(bool force_update = false) {
 if (local_grinders != null && !force_update) {
  return removeNulls(local_grinders);
 }
 filterLocalGrid < IMyShipGrinder > (local_grinders);
 foreach (var grinder in local_grinders) {
  consolidate(grinder.GetInventory(0));
 }
 return local_grinders;
}

List < IMyTerminalBlock > getWelders(bool force_update = false) {
 if (local_welders != null && !force_update) {
  return removeNulls(local_welders);
 }
 filterLocalGrid < IMyShipWelder > (local_welders);
 foreach (var welder in local_welders) {
  consolidate(welder.GetInventory(0));
 }
 return local_welders;
}

List < IMyTerminalBlock > getAirVents(bool force_update = false) {
 if (local_air_vents != null && !force_update) {
  return removeNulls(local_air_vents);
 }
 filterLocalGrid < IMyAirVent > (local_air_vents);
 return local_air_vents;
}

List < IMyTerminalBlock > getOxygenTanks(bool force_update = false) {
 if (local_oxygen_tanks != null && !force_update) {
  return removeNulls(local_oxygen_tanks);
 }
 filterLocalGrid < IMyOxygenTank > (local_oxygen_tanks);
 return local_oxygen_tanks;
}

List < IMyTerminalBlock > getHydrogenTanks(bool force_update = false) {
 if (local_hydrogen_tanks != null && !force_update) {
  return removeNulls(local_hydrogen_tanks);
 }
 filterLocalGrid < IMyOxygenTank > (local_hydrogen_tanks);
 return local_hydrogen_tanks;
}

List < IMyTerminalBlock > getOxygenGenerators(bool force_update = false) {
 if (local_oxygen_generators != null && !force_update) {
  return removeNulls(local_oxygen_generators);
 }
 filterLocalGrid < IMyOxygenGenerator > (local_oxygen_generators);
 return local_oxygen_generators;
}

// find which grid has a block at world_pos, excluding "self"
IMyCubeGrid findGrid(Vector3D world_pos, IMyCubeGrid self, List < IMyCubeGrid > grids) {
 foreach (var grid in grids) {
  if (grid == self) {
   continue;
  }
  var pos = grid.WorldToGridInteger(world_pos);
  if (grid.CubeExists(pos)) {
   return grid;
  }
 }
 return null;
}

IMyCubeGrid getConnectedGrid(IMyShipConnector connector) {
 if (!connector.IsConnected) {
  return null;
 }
 // skip connectors connecting to the same grid
 var other = connector.OtherConnector;
 if (other.CubeGrid == connector.CubeGrid) {
  return null;
 }
 return other.CubeGrid;
}

IMyCubeGrid getConnectedGrid(IMyMotorBase rotor, List < IMyCubeGrid > grids) {
 if (!rotor.IsAttached) {
  return null;
 }
 var position = rotor.Position;
 var orientation = rotor.Orientation;
 var direction = new Vector3I(0, 1, 0);
 Matrix matrix;
 orientation.GetMatrix(out matrix);
 Vector3I.Transform(ref direction, ref matrix, out direction);
 var world_pos = rotor.CubeGrid.GridIntegerToWorld(position + direction);
 return findGrid(world_pos, rotor.CubeGrid, grids);
}

IMyCubeGrid getConnectedGrid(IMyPistonBase piston, List < IMyCubeGrid > grids) {
 if (!piston.IsAttached) {
  return null;
 }
 var position = piston.Position;
 var orientation = piston.Orientation;
 bool is_large = piston.BlockDefinition.ToString().Contains("Large");
 var up = (int) Math.Round(piston.CurrentPosition / (is_large ? 2.5 : 0.5));
 var direction = new Vector3I(0, 2 + up, 0);
 Matrix matrix;
 orientation.GetMatrix(out matrix);
 Vector3I.Transform(ref direction, ref matrix, out direction);
 var world_pos = piston.CubeGrid.GridIntegerToWorld(position + direction);
 return findGrid(world_pos, piston.CubeGrid, grids);
}

// getting local grids is not trivial, we're basically building a graph of all
// grids and figure out which ones are local to us. we are also populating
// object lists in the meantime
List < IMyCubeGrid > getLocalGrids(bool force_update = false) {
 if (local_grids != null && !force_update) {
  return local_grids;
 }

 // clear all lists
 local_blocks = new List < IMyTerminalBlock > ();
 local_reactors = new List < IMyTerminalBlock > ();
 local_batteries = new List < IMyTerminalBlock > ();
 local_refineries = new List < IMyTerminalBlock > ();
 local_arc_furnaces = new List < IMyTerminalBlock > ();
 local_assemblers = new List < IMyTerminalBlock > ();
 local_connectors = new List < IMyTerminalBlock > ();
 local_storage = new List < IMyTerminalBlock > ();
 local_drills = new List < IMyTerminalBlock > ();
 local_grinders = new List < IMyTerminalBlock > ();
 local_welders = new List < IMyTerminalBlock > ();
 local_air_vents = new List < IMyTerminalBlock > ();
 local_oxygen_tanks = new List < IMyTerminalBlock > ();
 local_hydrogen_tanks = new List < IMyTerminalBlock > ();
 local_oxygen_generators = new List < IMyTerminalBlock > ();
 // piston and rotor lists are local, we don't need them once we're done
 var pistons = new List < IMyTerminalBlock > ();
 var rotors = new List < IMyTerminalBlock > ();

 // grid data for all the grids we discover
 var tmp_grid_data = new Dictionary < IMyCubeGrid, GridData > ();

 // get all blocks that are accessible to GTS
 GridTerminalSystem.GetBlocks(local_blocks);

 // for each block, get its grid, store data for this grid, and populate respective
 // object list if it's one of the objects we're interested in
 foreach (var block in local_blocks) {
  GridData data;
  if (!tmp_grid_data.TryGetValue(block.CubeGrid, out data)) {
   data = new GridData();
   tmp_grid_data.Add(block.CubeGrid, data);
  }

  // fill all lists
  if (block is IMyReactor) {
   local_reactors.Add(block);
  } else if (block is IMyBatteryBlock) {
   local_batteries.Add(block);
  } else if (block is IMyRefinery) {
   // refineries and furnaces are of the same type, but differ in definitions
   if (block.BlockDefinition.ToString().Contains("LargeRefinery")) {
    local_refineries.Add(block);
   } else {
    local_arc_furnaces.Add(block);
   }
  } else if (block is IMyAssembler) {
   local_assemblers.Add(block);
  } else if (block is IMyShipConnector) {
   local_connectors.Add(block);
  } else if (block is IMyCargoContainer) {
   local_storage.Add(block);
  } else if (block is IMyShipDrill) {
   local_drills.Add(block);
   data.has_drills = true;
  } else if (block is IMyShipGrinder) {
   local_grinders.Add(block);
   data.has_grinders = true;
  } else if (block is IMyShipWelder) {
   local_welders.Add(block);
   data.has_welders = true;
  } else if (block is IMyAirVent) {
   local_air_vents.Add(block);
  } else if (block is IMyOxygenTank) {
   // oxygen and hydrogen tanks are of the same type, but differ in definitions
   if (block.BlockDefinition.ToString().Contains("Hydrogen")) {
    local_hydrogen_tanks.Add(block);
   } else {
    local_oxygen_tanks.Add(block);
   }
  } else if (block is IMyOxygenGenerator) {
   local_oxygen_generators.Add(block);
  } else if (block is IMyPistonBase) {
   pistons.Add(block);
  } else if (block is IMyMotorBase) {
   rotors.Add(block);
  } else if (block is IMyMotorSuspension) {
   data.has_wheels = true;
  } else if (block is IMyThrust) {
   data.has_thrusters = true;
  } else if (block is IMyProgrammableBlock && block != Me && block.CubeGrid != Me.CubeGrid) {
   // skip disabled CPU's as well
   if (block.CustomName == "BARABAS Ship CPU") {
    data.override_ship = true;
   } else if (block.CustomName == "BARABAS Base CPU") {
    data.override_base = true;
   }
  }
 }

 // now, build a graph of all grids
 var graph = new GridGraph();
 var grids = new List < IMyCubeGrid > (tmp_grid_data.Keys);

 // first, go through all pistons
 foreach (IMyPistonBase piston in pistons) {
  var connected_grid = getConnectedGrid(piston, grids);

  if (connected_grid != null) {
   // grids connected to pistons are local to their source
   graph.addEdge(piston.CubeGrid, connected_grid, false).ToString();
  }
 }

 // do the same for rotors
 foreach (IMyMotorBase rotor in rotors) {
  var connected_grid = getConnectedGrid(rotor, grids);

  if (connected_grid != null) {
   // grids connected to locals are local to their source
   graph.addEdge(rotor.CubeGrid, connected_grid, false).ToString();
  }
 }

 // do the same for connectors
 foreach (IMyShipConnector connector in local_connectors) {
  var connected_grid = getConnectedGrid(connector);

  if (connected_grid != null) {
   // grids connected to connectors belong to a different ship
   graph.addEdge(connector.CubeGrid, connected_grid, true).ToString();
  }
 }

 // now, get our actual local grid
 local_grids = graph.getLocalGrids(Me.CubeGrid);

 // store our new local grid data
 local_grid_data = new GridData();
 foreach (var grid in local_grids) {
  local_grid_data.has_wheels |= tmp_grid_data[grid].has_wheels;
  local_grid_data.has_thrusters |= tmp_grid_data[grid].has_thrusters;
  local_grid_data.has_drills |= tmp_grid_data[grid].has_drills;
  local_grid_data.has_grinders |= tmp_grid_data[grid].has_grinders;
  local_grid_data.has_welders |= tmp_grid_data[grid].has_welders;
  local_grid_data.override_base |= tmp_grid_data[grid].override_base;
  local_grid_data.override_ship |= tmp_grid_data[grid].override_ship;
 }

 // now, go through all the connector-to-connector grid connections
 var connections = graph.getGridConnections();
 // we don't want to count known local grids as remote, so mark all local grids
 // as ones we've seen already
 var seen = new HashSet < IMyCubeGrid > (local_grids);

 remote_grid_data = new Dictionary < IMyCubeGrid, GridData > ();

 foreach (var e in connections) {
  // we may end up with two unknown grids
  var edge_grids = new List<IMyCubeGrid>(){e.src, e.dst};

  foreach (var e_g in edge_grids) {
   // if we found a new grid
   if (!seen.Contains(e_g)) {
    // get all grids that are local to it
    GridData data;
    var r_grids = graph.getLocalGrids(e_g);
    if (!remote_grid_data.TryGetValue(e_g, out data)) {
     data = new GridData();
    }
    // store their properties
    foreach (var g in r_grids) {
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
void findRemoteGrids() {
 if (remote_grid_data.Count == 0) {
  remote_base_grids = new List < IMyCubeGrid > ();
  remote_ship_grids = new List < IMyCubeGrid > ();
  connected_to_base = false;
  connected_to_ship = false;
  connected = false;
  return;
 }
 var base_grids = new List < IMyCubeGrid > ();
 var ship_grids = new List < IMyCubeGrid > ();
 // we need to know how many bases we've found
 var base_grid_info = new HashSet < GridData > ();

 foreach (var pair in remote_grid_data) {
  var grid = pair.Key;
  var data = pair.Value;
  if (data.override_ship) {
   ship_grids.Add(grid);
   continue;
  } else if (data.override_base) {
   base_grid_info.Add(data);
   base_grids.Add(grid);
   continue;
  }
  // if we're a base, assume every other grid is a ship unless we're explicitly
  // told that it's another base
  if (data.has_thrusters || data.has_wheels || isBaseMode()) {
   ship_grids.Add(grid);
  } else {
   base_grids.Add(grid);
   base_grid_info.Add(data);
  }
 }
 // we can't have multiple bases as we need to know where to push stuff
 if (isShipMode() && base_grid_info.Count > 1) {
  throw new BarabasException("Cannot have more than one base", this);
 }
 remote_base_grids = base_grids;
 remote_ship_grids = ship_grids;
 connected_to_base = base_grids.Count > 0;
 connected_to_ship = ship_grids.Count > 0;
 connected = connected_to_base || connected_to_ship;
}

List < IMyCubeGrid > getShipGrids() {
 return remote_ship_grids;
}

List < IMyCubeGrid > getRemoteGrids() {
 if (isBaseMode()) {
  return remote_ship_grids;
 } else {
  return remote_base_grids;
 }
}

List < IMyTerminalBlock > getRemoteStorage(bool force_update = false) {
 if (remote_storage != null && !force_update) {
  return removeNulls(remote_storage);
 }
 remote_storage = new List < IMyTerminalBlock > ();
 GridTerminalSystem.GetBlocksOfType < IMyCargoContainer > (remote_storage, remoteGridFilter);
 foreach (var storage in remote_storage) {
  consolidate(storage.GetInventory(0));
 }
 return remote_storage;
}

List < IMyTerminalBlock > getRemoteShipStorage(bool force_update = false) {
 if (remote_ship_storage != null && !force_update) {
  return removeNulls(remote_ship_storage);
 }
 remote_ship_storage = new List < IMyTerminalBlock > ();
 GridTerminalSystem.GetBlocksOfType < IMyCargoContainer > (remote_ship_storage, shipFilter);
 foreach (var storage in remote_ship_storage) {
  consolidate(storage.GetInventory(0));
 }
 return remote_ship_storage;
}

// get local trash disposal connector
List < IMyTerminalBlock > getTrashConnectors(bool force_update = false) {
 if (local_trash_connectors != null && !force_update) {
  return removeNulls(local_trash_connectors);
 }

 // find our group
 local_trash_connectors = new List < IMyTerminalBlock > ();

 var group = GridTerminalSystem.GetBlockGroupWithName("BARABAS Trash");

 if (group != null) {
  group.GetBlocks(local_trash_connectors);
 }

 // we may find multiple Trash groups, as we may have a BARABAS-driven
 // ships connected, so let's filter connectors
 filterLocalGrid < IMyShipConnector > (local_trash_connectors);

 // backwards compatibility: add old-style trash connectors as well
 var blocks = new List < IMyTerminalBlock > ();
 GridTerminalSystem.SearchBlocksOfName("BARABAS Trash", blocks, localGridFilter);

 foreach (var block in blocks) {
  if (!local_trash_connectors.Contains(block)) {
   local_trash_connectors.Add(block);
  }
 }

 // if we still have no trash connectors, use the first one available
 if (local_trash_connectors.Count == 0 && getConnectors().Count > 0) {
  local_trash_connectors.Add(getConnectors()[0]);
 }

 return local_trash_connectors;
}

// get local trash disposal sensors
List < IMyTerminalBlock > getTrashSensors(bool force_update = false) {
 if (local_trash_sensors != null && !force_update) {
  return removeNulls(local_trash_sensors);
 }

 // find our group
 local_trash_sensors = new List < IMyTerminalBlock > ();

 var group = GridTerminalSystem.GetBlockGroupWithName("BARABAS Trash");

 if (group != null) {
  group.GetBlocks(local_trash_sensors);
 }

 // we may find multiple Trash groups, as we may have a BARABAS-driven
 // ships connected, so let's filter sensors
 filterLocalGrid < IMySensorBlock > (local_trash_sensors);

 // backwards compatibility: add old-style BARABAS trash sensors as well
 var blocks = new List < IMyTerminalBlock > ();
 GridTerminalSystem.SearchBlocksOfName("BARABAS Trash Sensor", blocks, localGridFilter);

 foreach (var block in blocks) {
  if (!local_trash_sensors.Contains(block)) {
   local_trash_sensors.Add(block);
  }
 }

 return local_trash_sensors;
}

IMyTextPanel getConfigBlock(bool force_update = false) {
 if (!force_update && config_block != null) {
  if (!blockExists(config_block)) {
   return null;
  }
  return config_block;
 }
 var blocks = new List < IMyTerminalBlock > ();
 GridTerminalSystem.SearchBlocksOfName("BARABAS Config", blocks, localGridFilter);
 if (blocks.Count < 1) {
  return null;
 } else if (blocks.Count > 1) {
  Echo("Multiple config blocks found.");
  if (config_block != null) {
   // find our previous config block, ignore the rest
   var id = config_block.EntityId;
   config_block = GridTerminalSystem.GetBlockWithId(id) as IMyTextPanel;
  }
  if (config_block == null) {
   // if we didn't find our config block, just use the first one
   config_block = blocks[0] as IMyTextPanel;
  }
 } else {
  config_block = blocks[0] as IMyTextPanel;
 }
 return config_block;
}

/**
 * Inventory access functions
 */
// get all ingots of a certain type from a particular inventory
void getAllIngots(IMyTerminalBlock block, int srcInv, string name, List < ItemHelper > list) {
 var inv = block.GetInventory(srcInv);
 var items = inv.GetItems();
 for (int i = items.Count - 1; i >= 0; i--) {
  var item = items[i];
  if (!isIngot(item)) {
   continue;
  }
  if (name != null && item.Content.SubtypeName != name) {
   continue;
  }
  ItemHelper ih = new ItemHelper();
  ih.InvIdx = srcInv;
  ih.Item = item;
  ih.Index = i;
  ih.Owner = block;
  list.Add(ih);
 }
}

// get all ingots of a certain type from a particular inventory
void getAllOre(IMyTerminalBlock block, int srcInv, string name, List < ItemHelper > list) {
 var inv = block.GetInventory(srcInv);
 var items = inv.GetItems();
 for (int i = items.Count - 1; i >= 0; i--) {
  var item = items[i];
  if (!isOre(item)) {
   continue;
  }
  if (name != null && item.Content.SubtypeName != name) {
   continue;
  }
  ItemHelper ih = new ItemHelper();
  ih.InvIdx = srcInv;
  ih.Item = item;
  ih.Index = i;
  ih.Owner = block;
  list.Add(ih);
 }
}

// get all ingots residing in storage
List < ItemHelper > getAllStorageOre(string name = null) {
 List < ItemHelper > list = new List < ItemHelper > ();
 var blocks = getStorage();
 foreach (var block in blocks) {
  getAllOre(block, 0, name, list);
 }
 return list;
}

bool isOre(IMyInventoryItem item) {
 if (item.Content.SubtypeName == "Scrap") {
  return true;
 }
 return item.Content.TypeId.ToString().Equals("MyObjectBuilder_Ore");
}

bool isIngot(IMyInventoryItem item) {
 if (item.Content.SubtypeName == "Scrap") {
  return false;
 }
 return item.Content.TypeId.ToString().Equals("MyObjectBuilder_Ingot");
}

bool isComponent(IMyInventoryItem item) {
 return item.Content.TypeId.ToString().Equals("MyObjectBuilder_Component");
}

// get total amount of all ingots (of a particular type) stored in a particular inventory
Decimal getTotalIngots(IMyTerminalBlock block, int srcInv, string name) {
 var entries = new List < ItemHelper > ();
 getAllIngots(block, srcInv, name, entries);
 Decimal ingots = 0;
 foreach (var entry in entries) {
  var item = entry.Item;
  ingots += (Decimal) item.Amount;
 }
 return ingots;
}

/**
 * Inventory manipulation functions
 */
void consolidate(IMyInventory inv) {
 Dictionary < string, int > posmap = new Dictionary < string, int > ();
 var items = inv.GetItems();
 bool needs_consolidation = false;
 // go through all items and note the first time they appear in the inventory
 for (int i = 0; i < items.Count; i++) {
  var item = items[i];
  string str = item.Content.TypeId.ToString() + item.Content.SubtypeName;
  if (!posmap.ContainsKey(str)) {
   posmap[str] = i;
  } else {
   needs_consolidation = true;
  }
 }
 // make sure we don't touch already consolidated inventories
 if (!needs_consolidation) {
  return;
 }
 // now, consolidate all items
 for (int i = items.Count - 1; i >= 0; i--) {
  var item = items[i];
  string str = item.Content.TypeId.ToString() + item.Content.SubtypeName;
  int dstIndex = posmap[str];
  inv.TransferItemTo(inv, i, dstIndex, true, item.Amount);
 }
}

// make sure we process ore in chunks, prevent one ore clogging the refinery
void rebalance(IMyInventory inv) {
 // make note of how much was the first item
 Decimal ? first_amount = null;
 if (inv.GetItems().Count > 1) {
  first_amount = Math.Min((Decimal) inv.GetItems()[0].Amount, CHUNK_SIZE);
 }
 consolidate(inv);
 var items = inv.GetItems();
 // skip if we have no work to do
 if (items.Count < 2) {
  return;
 }

 for (int i = 0; i < Math.Min(items.Count, ore_types.Count); i++) {
  var item = items[i];

  // check if we have enough ore
  if ((Decimal) item.Amount > CHUNK_SIZE) {
   Decimal amount = 0;
   if (i == 0 && first_amount.HasValue) {
    amount = (Decimal) item.Amount - first_amount.Value;
   } else {
    amount = (Decimal) item.Amount - CHUNK_SIZE;
   }
   pushBack(inv, i, (VRage.MyFixedPoint) Math.Round(amount, 4));
  }
 }
}

Decimal TryTransfer(IMyTerminalBlock src, int srcInv, IMyTerminalBlock dst, int dstInv,
                    int srcIndex, int ? dstIndex, bool ? stack, VRage.MyFixedPoint ? amount) {
 var src_inv = src.GetInventory(srcInv);
 var dst_inv = dst.GetInventory(dstInv);
 var src_items = src_inv.GetItems();
 var src_count = src_items.Count;
 var src_amount = src_items[srcIndex].Amount;

 if (!src_inv.TransferItemTo(dst_inv, srcIndex, dstIndex, stack, amount)) {
  var sb = new StringBuilder();
  sb.Append("Error transfering from ");
  sb.Append(getBlockName(src));
  sb.Append(" to ");
  sb.Append(getBlockName(dst));
  Echo(sb.ToString());
  Echo("Check conveyors for missing/damage and\nblock ownership");
  return -1;
 }

 src_items = src_inv.GetItems();

 // if count changed, we transferred all of it
 if (src_count != src_items.Count) {
  return (Decimal) src_amount;
 }

 // if count didn't change, return the difference between src and cur amount
 var cur_amount = src_items[srcIndex].Amount;

 return (Decimal)(src_amount - cur_amount);
}

bool Transfer(IMyTerminalBlock src, int srcInv, IMyTerminalBlock dst, int dstInv,
              int srcIndex, int ? dstIndex, bool ? stack, VRage.MyFixedPoint ? amount) {
 if (src == dst) {
  return true;
 }
 return TryTransfer(src, srcInv, dst, dstInv, srcIndex, dstIndex, stack, amount) > 0;
}

void pushBack(IMyInventory src, int srcIndex, VRage.MyFixedPoint ? amount) {
 src.TransferItemTo(src, srcIndex, src.GetItems().Count, true, amount);
}

void pushFront(IMyInventory src, int srcIndex, VRage.MyFixedPoint ? amount) {
 src.TransferItemTo(src, srcIndex, 0, true, amount);
}

/**
 * Volume & storage load functions
 */
Decimal getTotalStorageLoad() {
 List < IMyTerminalBlock > storage;
 storage = getStorage();

 Decimal cur_volume = 0M;
 Decimal max_volume = 0M;
 Decimal ratio;
 foreach (var container in storage) {
  cur_volume += (Decimal) container.GetInventory(0).CurrentVolume;
  max_volume += (Decimal) container.GetInventory(0).MaxVolume;
 }
 ratio = Math.Round(cur_volume / max_volume, 2);

 if (isSpecializedShipMode()) {
  ratio = Math.Round(ratio * 0.75M, 4);
 } else {
  return ratio;
 }
 // if we're a drill ship or a grinder, also look for block with the biggest load
 if (isDrillMode()) {
  storage = getDrills();
 } else if (isGrinderMode()) {
  storage = getGrinders();
 } else if (isWelderMode()) {
  storage = getWelders();
 } else {
  throw new BarabasException("Unknown mode", this);
 }
 Decimal maxLoad = 0M;
 foreach (var container in storage) {
   var inv = container.GetInventory(0);
   var load = (Decimal) inv.CurrentVolume / (Decimal) inv.MaxVolume;
   if (load > maxLoad) {
    maxLoad = load;
   }
  }
  // scale the drill/grinder load to fit in the last 25% of the storage
  // the result of this is, when the storage is full, yellow alert goes off,
  // when drills/grinders are full, red alert goes off
 ratio = ratio + maxLoad * 0.25M;
 return ratio;
}

// we are interested only in stuff stored in storage and in tools
Decimal getTotalStorageMass() {
 var storage = new List<IMyTerminalBlock>();
 storage.AddRange(getStorage());
 storage.AddRange(getDrills());
 storage.AddRange(getWelders());
 storage.AddRange(getGrinders());

 Decimal cur_mass = 0M;
 foreach (var container in storage) {
  cur_mass += (Decimal) container.GetInventory(0).CurrentMass;
 }
 return cur_mass;
}

// decide if a refinery can accept certain ore - this is done to prevent
// clogging all refineries with single ore
bool canAcceptOre(IMyInventory inv, string name) {
 Decimal volumeLeft = ((Decimal) inv.MaxVolume - (Decimal) inv.CurrentVolume) * 1000M;
 if (volumeLeft > 1500M) {
  return true;
 }
 if (volumeLeft < 100M || name == ICE) {
  return false;
 }

 // if this is a priority ore, accept it unconditionally
 if (can_use_ingots && storage_ingot_status[name] < material_thresholds[name]) {
  return true;
 }
 // if no ore is priority, don't clog the refinery
 if (volumeLeft < 600M) {
  return false;
 }

 // aim for equal spread
 Dictionary < string, Decimal > ores = new Dictionary < string, Decimal > ();
 var items = inv.GetItems();
 bool seenCurrent = false;
 foreach (var item in items) {
  var ore = item.Content.SubtypeName;
  Decimal amount;
  ores.TryGetValue(ore, out amount);
  ores[ore] = amount + (Decimal) item.Amount;
  if (ore == name) {
   seenCurrent = true;
  }
 }
 int keyCount = ores.Keys.Count;
 if (!seenCurrent) {
  keyCount++;
 }
 // don't clog refinery with single ore
 if (keyCount < 2) {
  if (volumeLeft < 1000M) {
   return false;
  } else {
   return true;
  }
 }
 Decimal cur_amount;
 ores.TryGetValue(name, out cur_amount);
 Decimal target_amount = ((Decimal) inv.CurrentVolume * 1000M) / keyCount;
 return cur_amount < target_amount;
}

bool hasOnlyOre(IMyInventory inv) {
 var items = inv.GetItems();
 foreach (var item in items) {
  if (!isOre(item)) {
   return false;
  }
 }
 return true;
}

bool hasOnlyIngots(IMyInventory inv) {
 var items = inv.GetItems();
 foreach (var item in items) {
  if (!isIngot(item)) {
   return false;
  }
 }
 return true;
}

bool hasOnlyComponents(IMyInventory inv) {
 var items = inv.GetItems();
 foreach (var item in items) {
  // we don't care about components specifically; rather, we care if it
  // has ore or ingots
  if (isOre(item) || isIngot(item)) {
   return false;
  }
 }
 return true;
}

void checkStorageLoad() {
 if (getStorage().Count == 0) {
  status_report[STATUS_STORAGE_LOAD] = "No storage found";
  removeAlert(YELLOW_ALERT);
  removeAlert(RED_ALERT);
  return;
 }
 Decimal storageLoad = getTotalStorageLoad();
 if (storageLoad >= 0.98M) {
  addAlert(RED_ALERT);
  removeAlert(YELLOW_ALERT);
  // if we're a base, enter crisis mode
  if (isBaseMode() && has_refineries && refineriesClogged()) {
   if (tried_throwing) {
    storeTrash(true);
    crisis_mode = CRISIS_MODE_LOCKUP;
   } else {
    crisis_mode = CRISIS_MODE_THROW_ORE;
   }
  }
 } else {
  removeAlert(RED_ALERT);
 }

 if (crisis_mode == CRISIS_MODE_THROW_ORE && storageLoad < 0.98M) {
  // exit crisis mode, but "tried_throwing" still reminds us that we
  // have just thrown out ore - if we end up in a crisis again, we'll
  // go lockup instead of throwing ore
  crisis_mode = CRISIS_MODE_NONE;
  storeTrash(true);
 }
 if (storageLoad >= 0.75M && storageLoad < 0.98M) {
  storeTrash();
  addAlert(YELLOW_ALERT);
 } else if (storageLoad < 0.75M) {
  removeAlert(YELLOW_ALERT);
  storeTrash();
  if (storageLoad < 0.98M && isBaseMode()) {
   tried_throwing = false;
  }
 }
 Decimal mass = getTotalStorageMass();
 int idx = 0;
 string suffixes = " kMGTPEZY";
 while (mass >= 1000M) {
  mass /= 1000M;
  idx++;
 }
 mass = Math.Round(mass, 1);
 char suffix = suffixes[idx];

 status_report[STATUS_STORAGE_LOAD] = String.Format("{0}% / {1}{2}",
  Math.Round(storageLoad * 100M, 0), mass, suffix);
}

string getPowerLoadStr(Decimal value) {
 var pwrs = "kMGTPEZY";
 int pwr_idx = 0;
 while (value >= 1000M) {
  value /= 1000M;
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
void sortLocalStorage() {
 var containers = getStorage();
 // don't sort if there are less than three containers
 if (containers.Count < 3) {
  return;
 }
 int sorted_items = 0;

 // for each container, transfer one item
 for (int c = 0; c < containers.Count; c++) {
  var container = containers[c];
  var inv = container.GetInventory(0);
  var items = inv.GetItems();
  var curVolume = inv.CurrentVolume;

  for (int i = items.Count - 1; i >= 0; i--) {
   var item = items[i];
   bool itemIsOre = isOre(item);
   bool itemIsIngot = isIngot(item);
   bool itemIsComponent = !itemIsOre && !itemIsIngot;

   // don't try sorting already sorted items
   if (c % 3 == 0 && itemIsOre) {
    continue;
   } else if (c % 3 == 1 && itemIsIngot) {
    continue;
   } else if (c % 3 == 2 && itemIsComponent) {
    continue;
   }

   pushToStorage(container, 0, i, null);

   // we don't check for success because we may end up sending stuff to
   // the same container; rather, we check if volume has changed
   if (curVolume != inv.CurrentVolume) {
    sorted_items++;
    curVolume = inv.CurrentVolume;
   }
   // sort ten items per run
   if (sorted_items == 10) {
    break;
   }
  }
 }
}

// try pushing something to one of the local storage containers
bool pushToStorage(IMyTerminalBlock block, int invIdx, int srcIndex, VRage.MyFixedPoint ? amount) {
 var containers = getStorage();
 /*
  * Stage 0: special case for small container numbers, or if sorting is
  * disabled. Basically, don't sort.
  */

 if (containers.Count < 3 || !sort_storage) {
  foreach (var storage in containers) {
   var container_inv = storage.GetInventory(0);
   // try pushing to this container
   if (Transfer(block, invIdx, storage, 0, srcIndex, null, true, amount)) {
    return true;
   }
  }
  return false;
 }

 /*
  * Stage 1: try to put stuff into designated containers
  */
 var src = block.GetInventory(invIdx);
 var item = src.GetItems()[srcIndex];
 bool itemIsOre = isOre(item);
 bool itemIsIngot = isIngot(item);
 bool itemIsComponent = !itemIsOre && !itemIsIngot;
 int startStep;
 if (itemIsOre) {
  startStep = 0;
 } else if (itemIsIngot) {
  startStep = 1;
 } else {
  startStep = 2;
 }
 int steps = 3;
 for (int i = startStep; i < containers.Count; i += steps) {
  var container_inv = containers[i].GetInventory(0);
  // try pushing to this container
  if (Transfer(block, invIdx, containers[i], 0, srcIndex, null, true, amount)) {
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
 Decimal maxFreeVolume = 0M;
 for (int i = 0; i < containers.Count; i++) {
  // skip containers we already saw in a previous loop
  if (i % steps == startStep) {
   continue;
  }

  // skip full containers
  var container_inv = containers[i].GetInventory(0);
  Decimal freeVolume = ((Decimal) container_inv.MaxVolume - (Decimal) container_inv.CurrentVolume) * 1000M;
  if (freeVolume < 1M) {
   continue;
  }

  if (emptyIdx == -1 && container_inv.CurrentVolume == 0) {
   emptyIdx = i;
   continue;
  }
  if (overflowIdx == -1) {
   bool isOverflow = false;
   if (itemIsOre && hasOnlyOre(container_inv)) {
    isOverflow = true;
   } else if (itemIsIngot && hasOnlyIngots(container_inv)) {
    isOverflow = true;
   } else if (itemIsComponent && hasOnlyComponents(container_inv)) {
    isOverflow = true;
   }
   if (isOverflow) {
    overflowIdx = i;
    continue;
   }
  }
  if (freeVolume > maxFreeVolume) {
   leastFullIdx = i;
   maxFreeVolume = freeVolume;
  }
 }

 // now, try pushing into one of the containers we found
 if (overflowIdx != -1) {
  var dst = containers[overflowIdx];
  if (Transfer(block, invIdx,  dst, 0, srcIndex, null, true, amount)) {
   return true;
  }
 }
 if (emptyIdx != -1) {
  var dst = containers[emptyIdx];
  if (Transfer(block, invIdx, dst, 0, srcIndex, null, true, amount)) {
   return true;
  }
 }
 if (leastFullIdx != -1) {
  var dst = containers[leastFullIdx];
  if (Transfer(block, invIdx, dst, 0, srcIndex, null, true, amount)) {
   return true;
  }
 }
 return false;
}

// try pushing something to one of the remote storage containers
bool pushToRemoteStorage(IMyTerminalBlock block, int srcInv, int srcIndex, VRage.MyFixedPoint ? amount) {
 var containers = getRemoteStorage();
 foreach (var container in containers) {
  var container_inv = container.GetInventory(0);
  Decimal freeVolume = ((Decimal) container_inv.MaxVolume - (Decimal) container_inv.CurrentVolume) * 1000M;
  if (freeVolume < 1M) {
   continue;
  }
  // try pushing to this container
  if (Transfer(block, srcInv, container, 0, srcIndex, null, true, amount)) {
   return true;
  }
 }
 return false;
}

// try pushing something to one of the remote storage containers
bool pushToRemoteShipStorage(IMyTerminalBlock block, int srcInv, int srcIndex, VRage.MyFixedPoint ? amount) {
 var containers = getRemoteShipStorage();
 foreach (var container in containers) {
  // try pushing to this container
  if (Transfer(block, srcInv, container, 0, srcIndex, null, true, amount)) {
   return true;
  }
 }
 return false;
}

// send everything from local storage to remote storage
void pushAllToRemoteStorage() {
 var storage = getStorage();
 foreach (var container in storage) {
  var inv = container.GetInventory(0);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   var item = items[j];
   if (isOre(item) && push_ore_to_base) {
    pushToRemoteStorage(container, 0, j, null);
   }
   if (isIngot(item)) {
    var type = item.Content.SubtypeName;
    if (type != URANIUM && push_ingots_to_base) {
     pushToRemoteStorage(container, 0, j, null);
    }
   }
   if (isComponent(item) && push_components_to_base) {
    pushToRemoteStorage(container, 0, j, null);
   }
  }
 }
}

// pull everything from remote storage
void pullFromRemoteStorage() {
 if (isBaseMode()) {
  return;
 }
 var storage = getRemoteStorage();
 foreach (var container in storage) {
  var inv = container.GetInventory(0);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   var item = items[j];
   if (isOre(item) && pull_ore_from_base) {
    pushToStorage(container, 0, j, null);
   }
   if (isIngot(item)) {
    var type = item.Content.SubtypeName;
    // don't take all uranium from base
    if (type == URANIUM && auto_refuel_ship && !powerAboveHighWatermark()) {
     pushToStorage(container, 0, j, (VRage.MyFixedPoint) Math.Min(0.5M, (Decimal) item.Amount));
    } else if (type != URANIUM && pull_ingots_from_base) {
     pushToStorage(container, 0, j, null);
    }
   }
   if (isComponent(item) && pull_components_from_base) {
    pushToStorage(container, 0, j, null);
   }
  }
 }
}

// push everything to ship
void pushToRemoteShipStorage() {
 var storage = getStorage();
 foreach (var container in storage) {
  var inv = container.GetInventory(0);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   var item = items[j];
   if (isOre(item) && pull_ore_from_base) {
    pushToRemoteShipStorage(container, 0, j, null);
   }
   if (isIngot(item)) {
    var type = item.Content.SubtypeName;
    if (type != URANIUM && pull_ingots_from_base) {
     pushToRemoteShipStorage(container, 0, j, null);
    }
   }
   if (isComponent(item) && pull_components_from_base) {
    pushToRemoteShipStorage(container, 0, j, null);
   }
  }
 }
}

// get everything from ship
void pullFromRemoteShipStorage() {
 var storage = getRemoteShipStorage();
 foreach (var container in storage) {
  var inv = container.GetInventory(0);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   var item = items[j];
   if (isOre(item) && push_ore_to_base) {
    pushToStorage(container, 0, j, null);
   }
   if (isIngot(item)) {
    var type = item.Content.SubtypeName;
    // don't take all uranium from base
    if (type != URANIUM && push_ingots_to_base) {
     pushToStorage(container, 0, j, null);
    }
   }
   if (isComponent(item) && push_components_to_base) {
    pushToStorage(container, 0, j, null);
   }
  }
 }
}

// push everything in every block to local storage
void emptyBlocks(List < IMyTerminalBlock > blocks) {
 foreach (var block in blocks) {
  var inv = block.GetInventory(0);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   pushToStorage(block, 0, j, null);
  }
 }
}

// push stuff to welders
void fillWelders() {
 var welders = getWelders();
 int s_index = 0;

 foreach (var welder in welders) {
  Decimal cur_vol = (Decimal) welder.GetInventory(0).CurrentVolume * 1000M;
  Decimal max_vol = (Decimal) welder.GetInventory(0).MaxVolume * 1000M;
  Decimal target_volume = max_vol - 400M - cur_vol;
  if (target_volume <= 0) {
   continue;
  }
  var dst_inv = welder.GetInventory(0);
  var storage = getStorage();
  for (; s_index < storage.Count; s_index++) {
   var container = storage[s_index];
   var src_inv = container.GetInventory(0);
   var items = src_inv.GetItems();
   for (int j = items.Count - 1; j >= 0; j--) {
    var item = items[j];
    if (!isComponent(item)) {
     continue;
    }
    if (target_volume <= 0) {
     break;
    }
    Decimal amount = (Decimal) item.Amount - 1M;
    // if it's peanuts, just send out everthing
    if (amount < 2M) {
     src_inv.TransferItemTo(dst_inv, j, null, true, null);
     continue;
    }

    // send one and check load
    Decimal old_vol = (Decimal) dst_inv.CurrentVolume * 1000M;
    if (!Transfer(welder, 0, container, 0, j, null, true, (VRage.MyFixedPoint) 1)) {
     continue;
    }
    Decimal new_vol = (Decimal) dst_inv.CurrentVolume * 1000M;
    Decimal item_vol = new_vol - old_vol;
    int target_amount = (int) Math.Floor(target_volume / item_vol);
    src_inv.TransferItemTo(dst_inv, j, null, true, (VRage.MyFixedPoint) target_amount);
    target_volume -= Math.Min(target_amount, amount) * item_vol;
   }
   if (target_volume <= 0) {
    break;
   }
  }
 }
}

// push all ore from refineries to storage
void pushOreToStorage() {
 var refineries = getAllRefineries();
 foreach (var refinery in refineries) {
  var inv = refinery.GetInventory(0);
  for (int j = inv.GetItems().Count - 1; j >= 0; j--) {
   pushToStorage(refinery, 0, j, null);
  }
 }
}

// push ice from refineries to storage (we never push ice from oxygen generators)
void pushIceToStorage() {
 var refineries = getRefineries();
 foreach (var refinery in refineries) {
  var inv = refinery.GetInventory(0);
  for (int j = inv.GetItems().Count - 1; j >= 0; j--) {
   var item = inv.GetItems()[j];
   if (item.Content.SubtypeName != ICE) {
    continue;
   }
   pushToStorage(refinery, 0, j, null);
  }
 }
}

/**
 * Uranium, reactors & batteries
 */
Decimal getMaxReactorPowerOutput(bool force_update = false) {
 if (!force_update) {
  return max_reactor_output;
 }

 max_reactor_output = 0;
 var reactors = getReactors();
 foreach (IMyReactor reactor in reactors) {
  max_reactor_output += (Decimal) reactor.MaxOutput * 1000M;
 }

 return max_reactor_output;
}

Decimal getCurReactorPowerOutput(bool force_update = false) {
 if (!force_update) {
  return cur_reactor_output;
 }

 cur_reactor_output = 0;
 var reactors = getReactors();
 foreach (IMyReactor reactor in reactors) {
  if (reactor.IsWorking)
   cur_reactor_output += (Decimal) reactor.MaxOutput * 1000M;
 }

 return cur_reactor_output;
}

Decimal getMaxBatteryPowerOutput(bool force_update = false) {
 if (!force_update) {
  return max_battery_output;
 }

 max_battery_output = 0;
 var batteries = getBatteries();
 foreach (IMyBatteryBlock battery in batteries) {
  if (battery.HasCapacityRemaining) {
   // there's no API function to provide this information, and parsing
   // DetailedInfo is kinda overkill for this, so just hard-code the value
   max_battery_output += large_grid ? 12000M : 4320M;
  }
 }

 return max_battery_output;
}

Decimal getBatteryStoredPower() {
 var batteries = getBatteries();
 Decimal stored_power = 0;
 foreach (IMyBatteryBlock battery in batteries) {
  // unlike reactors, batteries' kWh are _actual_ kWh, not kWm
  stored_power += (Decimal) battery.CurrentStoredPower * 1000M * 60M;
 }
 return stored_power;
}

Decimal getReactorStoredPower() {
 if (has_reactors) {
  return URANIUM_INGOT_POWER * ingot_status[URANIUM];
 }
 return 0;
}

// since blocks don't report their power draw, we look at what reactors/batteries
// are outputting instead. we don't count solar as those are transient power sources
Decimal getCurPowerDraw(bool force_update = false) {
 if (!force_update) {
  return cur_power_draw;
 }

 Decimal power_draw = 0;

 // go through all reactors and batteries
 foreach (IMyReactor block in getReactors()) {
  power_draw += (Decimal) block.CurrentOutput * 1000M;
 }
 foreach (IMyBatteryBlock block in getBatteries()) {
  power_draw += (Decimal) (block.CurrentOutput - block.CurrentInput) * 1000M;
 }

 cur_power_draw = power_draw;

 return cur_power_draw;
}

// blocks don't report their max power draw, so we're forced to parse DetailedInfo
Decimal getMaxPowerDraw(bool force_update = false) {
 if (!force_update) {
  return max_power_draw;
 }

 Decimal power_draw = 0;

 // go through all the blocks
 foreach (var block in getBlocks()) {
   if (block is IMyBatteryBlock)
    continue;
   // if this is a thruster
   if (block is IMyThrust) {
    var typename = block.BlockDefinition.ToString();
    Decimal thrust_draw;
    bool found = thrust_power.TryGetValue(typename, out thrust_draw);
    if (found) {
     power_draw += thrust_draw;
    } else {
     thrust_draw = getBlockPowerUse(block);
     if (thrust_draw == 0) {
      throw new BarabasException("Unknown thrust type", this);
     } else {
      power_draw += thrust_draw;
     }
    }
   }
   // it's a regular block
   else {
    power_draw += getBlockPowerUse(block);
   }
  }
  // add 5% to account for various misc stuff like conveyors etc
 power_draw *= 1.05M;

 if (getMaxBatteryPowerOutput() + getCurReactorPowerOutput() == 0) {
  max_power_draw = power_draw;
 } else {
  // now, check if we're not overflowing the reactors and batteries
  max_power_draw = Math.Min(power_draw, getMaxBatteryPowerOutput() + getCurReactorPowerOutput());
 }

 return max_power_draw;
}

// parse DetailedInfo for power use information - this shouldn't exist, but
// the API is deficient, sooo...
Decimal getBlockPowerUse(IMyTerminalBlock block) {
 var power_regex = new System.Text.RegularExpressions.Regex("Max Required Input: ([\\d\\.]+) (\\w?)W");
 var cur_regex = new System.Text.RegularExpressions.Regex("Current Input: ([\\d\\.]+) (\\w?)W");
 var power_match = power_regex.Match(block.DetailedInfo);
 var cur_match = cur_regex.Match(block.DetailedInfo);
 if (!power_match.Success && !cur_match.Success) {
  return 0;
 }

 Decimal cur = 0, max = 0;
 if (power_match.Groups[1].Success && power_match.Groups[2].Success) {
  bool result = Decimal.TryParse(power_match.Groups[1].Value, out max);
  if (!result) {
   throw new BarabasException("Invalid detailed info format!", this);
  }
  max *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(power_match.Groups[2].Value) - 1);
 }
 if (cur_match.Groups[1].Success && cur_match.Groups[2].Success) {
  bool result = Decimal.TryParse(cur_match.Groups[1].Value, out cur);
  if (!result) {
   throw new BarabasException("Invalid detailed info format!", this);
  }
  cur *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(cur_match.Groups[2].Value) - 1);
 }
 return Math.Max(cur, max);
}

Decimal getPowerHighWatermark(Decimal power_use) {
 return power_use * power_high_watermark;
}

Decimal getPowerLowWatermark(Decimal power_use) {
 return power_use * power_low_watermark;
}

bool powerAboveHighWatermark() {
 var stored_power = getBatteryStoredPower() + getReactorStoredPower();

 // check if we have enough uranium ingots to fill all local reactors and
 // have a few spare ones
 Decimal power_draw;
 if (isShipMode() && connected_to_base) {
  power_draw = getMaxPowerDraw();
 } else {
  power_draw = getCurPowerDraw();
 }
 Decimal power_needed = getPowerHighWatermark(power_draw);
 Decimal totalPowerNeeded = power_needed * 1.3M;

 if (stored_power > totalPowerNeeded) {
  power_above_threshold = true;
  return true;
 }
 // if we always go by fixed limit, we will constantly have to refine uranium.
 // therefore, rather than constantly refining uranium, let's watch a certain
 // threshold and allow for other ore to be refined while we still have lots of
 // spare uranium
 if (stored_power > power_needed && power_above_threshold) {
  return true;
 }
 // we flip the switch, so next time we decide it's time to leave uranium alone
 // will be when we have uranium above threshold
 power_above_threshold = false;

 return false;
}

// check if we don't have much power left
bool powerAboveLowWatermark() {
 Decimal power_draw;
 if (isShipMode() && connected_to_base) {
  power_draw = getMaxPowerDraw();
 } else {
  power_draw = getCurPowerDraw();
 }
 return getBatteryStoredPower() + getReactorStoredPower() > getPowerLowWatermark(power_draw);
}

// push uranium into reactors, optionally push ALL uranium into reactors
bool refillReactors(bool force = false) {
 bool refilled = true;
 ItemHelper ingot = null;
 Decimal orig_amount = 0M, cur_amount = 0M;
 int s_index = 0;
 // check if we can put some more uranium into reactors
 var reactors = getReactors();
 foreach (IMyReactor reactor in reactors) {
  var rinv = reactor.GetInventory(0);
  Decimal reactor_proportion = (Decimal) reactor.MaxOutput * 1000M / getMaxReactorPowerOutput();
  Decimal reactor_power_draw = getMaxPowerDraw() * (reactor_proportion);
  Decimal ingots_per_reactor = getPowerHighWatermark(reactor_power_draw) / URANIUM_INGOT_POWER;
  Decimal ingots_in_reactor = getTotalIngots(reactor, 0, URANIUM);
  if ((ingots_in_reactor < ingots_per_reactor) || force) {
   // find us an ingot
   if (ingot == null) {
    var storage = getStorage();
    for (; s_index < storage.Count; s_index++) {
     var sinv = storage[s_index].GetInventory(0);
     var items = sinv.GetItems();
     for (int j = 0; j < items.Count; j++) {
      var item = items[j];
      if (isIngot(item) && item.Content.SubtypeName == URANIUM) {
       ingot = new ItemHelper();
       ingot.InvIdx = 0;
       ingot.Index = j;
       ingot.Item = item;
       ingot.Owner = storage[s_index];
       orig_amount = (Decimal) item.Amount;
       cur_amount = orig_amount;
       break;
      }
     }
     if (ingot != null) {
      break;
     }
    }
    // if we didn't find any ingots
    if (ingot == null) {
     return false;
    }
   }
   Decimal amount;
   Decimal proportional_amount = (Decimal) Math.Round(orig_amount * reactor_proportion, 4);
   if (force) {
    amount = proportional_amount;
   } else {
    amount = (Decimal) Math.Min(proportional_amount, ingots_per_reactor - ingots_in_reactor);
   }

   // don't leave change, we've expended this ingot
   if (cur_amount - amount <= 0.05M) {
    cur_amount = 0;
    rinv.TransferItemFrom(ingot.Owner.GetInventory(ingot.InvIdx), ingot.Index, null, true, null);
    ingot = null;
   } else {
    amount = TryTransfer(ingot.Owner, ingot.InvIdx, reactor, 0, ingot.Index, null, true, (VRage.MyFixedPoint) amount);
    if (amount > 0) {
     cur_amount -= amount;
    }
   }
   if (ingots_in_reactor + amount < ingots_per_reactor) {
    refilled = false;
   }
  }
 }
 return refilled;
}

// push uranium to storage if we have too much of it in reactors
void pushSpareUraniumToStorage() {
 var reactors = getReactors();
 foreach (IMyReactor reactor in reactors) {
  var inv = reactor.GetInventory(0);
  if (inv.GetItems().Count > 1) {
   consolidate(inv);
  }
  Decimal ingots = getTotalIngots(reactor, 0, URANIUM);
  Decimal reactor_power_draw = getMaxPowerDraw() *
   (((Decimal) reactor.MaxOutput * 1000M) / (getMaxReactorPowerOutput() + getMaxBatteryPowerOutput()));
  Decimal ingots_per_reactor = getPowerHighWatermark(reactor_power_draw);
  if (ingots > ingots_per_reactor) {
   Decimal amount = ingots - ingots_per_reactor;
   pushToStorage(reactor, 0, 0, (VRage.MyFixedPoint) amount);
  }
 }
}

/**
 * Trash
 */
bool startThrowing(IMyShipConnector connector, bool force = false) {
 // if connector is locked, it's in use, so don't do anything
 if (connector.IsLocked) {
  return false;
 }
 if (!force && hasUsefulItems(connector)) {
  return false;
 }
 if (!connector.ThrowOut) {
  connector.ApplyAction("ThrowOut");
 }

 return true;
}

void stopThrowing(IMyShipConnector connector) {
 // at this point, we have already turned off the conveyors,
 // so now just check if we are throwing and if we are in
 // "collect all" mode
 if (connector.CollectAll) {
  // disable collect all
  connector.ApplyAction("CollectAll");
 }
 // disable throw out
 if (connector.ThrowOut) {
  connector.ApplyAction("ThrowOut");
 }
}

void startThrowing(bool force = false) {
 foreach (IMyShipConnector connector in getTrashConnectors()) {
  startThrowing(connector, force);
 }
}

void stopThrowing() {
 foreach (IMyShipConnector connector in getTrashConnectors()) {
  stopThrowing(connector);
 }
}

// only stone is considered unuseful, and only if config says to throw out stone
bool isUseful(IMyInventoryItem item) {
 if (!isOre(item)) {
  return true;
 }
 return !throw_out_stone || item.Content.SubtypeName != STONE;
}

bool hasUsefulItems(IMyShipConnector connector) {
 var inv = connector.GetInventory(0);
 foreach (var item in inv.GetItems()) {
  if (isUseful(item)) {
   return true;
  }
 }
 return false;
}

bool trashHasUsefulItems() {
 foreach (IMyShipConnector connector in getTrashConnectors()) {
  if (hasUsefulItems(connector)) {
   return true;
  }
 }
 return false;
}

bool trashSensorsActive() {
 var sensors = getTrashSensors();
 foreach (IMySensorBlock sensor in sensors) {
  if (sensor.IsActive) {
   return true;
  }
 }
 return false;
}

bool throwOutOre(string name, Decimal ore_amount = 0, bool force = false) {
 var connectors = getTrashConnectors();
 var skip_list = new List < IMyTerminalBlock > ();

 if (connectors.Count == 0) {
  return false;
 }
 Decimal orig_target = ore_amount == 0 ? 5 * CHUNK_SIZE : ore_amount;
 Decimal target_amount = orig_target;

 // first, go through list of connectors and enable throw
 foreach (IMyShipConnector connector in connectors) {
  if (!startThrowing(connector, force)) {
   skip_list.Add(connector);
   continue;
  }
 }

 // now, find all instances of ore we're looking for, and push it to trash
 var entries = getAllStorageOre(name);
 foreach (var entry in entries) {
  var item = entry.Item;
  var srcObj = entry.Owner;
  var invIdx = entry.InvIdx;
  var index = entry.Index;
  var orig_amount = Math.Min(target_amount, (Decimal) entry.Item.Amount);
  var cur_amount = orig_amount;

  foreach (IMyShipConnector connector in connectors) {
   if (skip_list.Contains(connector)) {
    continue;
   }
   var amount = Math.Min(orig_amount / getTrashConnectors().Count, cur_amount);

   // send it to connector
   var transferred = TryTransfer(srcObj, invIdx, connector, 0, index, null, true,
    (VRage.MyFixedPoint) amount);
   if (transferred > 0) {
    target_amount -= transferred;
    cur_amount -= transferred;

    if (target_amount == 0) {
     return true;
    }
    if (cur_amount == 0) {
     break;
    }
   }
  }
 }
 return target_amount != orig_target || entries.Count == 0;
}

// move everything (or everything excluding stone) from trash to storage
void storeTrash(bool store_all = false) {
 int count = 0;
 if (store_all || trashHasUsefulItems()) {
  stopThrowing();
 }
 var connectors = getTrashConnectors();
 foreach (var connector in connectors) {
  var inv = connector.GetInventory(0);
  consolidate(inv);
  var items = inv.GetItems();
  for (int i = items.Count - 1; i >= 0; i--) {
   var item = items[i];
   if (!store_all && !isUseful(item)) {
    continue;
   }
   // do this ten times
   if (pushToStorage(connector, 0, i, null) && ++count == 10) {
    return;
   }
  }
 }
}

/**
 * Ore and refineries
 */
void refineOre() {
 refine_ice = !iceAboveHighWatermark();
 var items = getAllStorageOre();
 foreach (var item in items) {
  List < IMyTerminalBlock > refineries;
  string ore = item.Item.Content.SubtypeName;
  if (ore == SCRAP) {
   ore = IRON;
  }
  // ice is preferably refined with oxygen generators, and only if we need to
  if (ore == ICE) {
   refineries = getOxygenGenerators();
   if (refineries.Count == 0) {
    if (!refine_ice) {
     continue;
    }
    refineries = getRefineries();
   }
  } else if (arc_furnace_ores.Contains(ore)) {
   refineries = getAllRefineries();
  } else {
   refineries = getRefineries();
  }
  if (refineries.Count == 0) {
   continue;
  }

  Decimal orig_amount = Math.Round((Decimal) item.Item.Amount / (Decimal) refineries.Count, 4);
  Decimal amount = (Decimal) Math.Min(CHUNK_SIZE, orig_amount);
  // now, go through every refinery and do the transfer
  for (int r = 0; r < refineries.Count; r++) {
   // if we're last in the list, send it all
   if (r == refineries.Count - 1 && amount < CHUNK_SIZE) {
    amount = 0;
   }
   var refinery = refineries[r];
   removeBlockAlert(refinery, ALERT_CLOGGED);
   var input_inv = refinery.GetInventory(0);
   var output_inv = refinery.GetInventory(1);
   Decimal input_load = (Decimal) input_inv.CurrentVolume / (Decimal) input_inv.MaxVolume;
   if (canAcceptOre(input_inv, ore) || ore == ICE) {
    // if we've got a very small amount, send it all
    var item_inv = item.Owner.GetInventory(item.InvIdx);
    if (amount < 1) {
     if (Transfer(item.Owner, item.InvIdx, refinery, 0, item.Index, input_inv.GetItems().Count, true, null)) {
      break;
     }
    }
    // if refinery is almost empty, send a lot
    else if (input_load < 0.2M) {
     amount = Math.Min(CHUNK_SIZE * 5, orig_amount);
     item_inv.TransferItemTo(input_inv, item.Index, input_inv.GetItems().Count, true, (VRage.MyFixedPoint) amount);
    } else {
     item_inv.TransferItemTo(input_inv, item.Index, input_inv.GetItems().Count, true, (VRage.MyFixedPoint) amount);
    }
   }
  }
 }
}

// find which ore needs prioritization the most
void reprioritizeOre() {
 string low_wm_ore = null;
 string high_wm_ore = null;
 string low_wm_arc_ore = null;
 string high_wm_arc_ore = null;

 // if we know we want uranium, prioritize it
 if (prioritize_uranium && ore_status[URANIUM] > 0) {
  low_wm_ore = URANIUM;
 }
 // find us ore to prioritize (hi and low, regular and arc furnace)
 foreach (var ore in ore_types) {
  if (ore == ICE) {
   // ice is refined separately
   continue;
  }
  bool arc = arc_furnace_ores.Contains(ore);
  if (low_wm_ore == null && storage_ingot_status[ore] < material_thresholds[ore] && ore_status[ore] > 0) {
   low_wm_ore = ore;
  } else if (high_wm_ore == null && storage_ingot_status[ore] < (material_thresholds[ore] * 5) && ore_status[ore] > 0) {
   high_wm_ore = ore;
  }
  if (arc && low_wm_arc_ore == null && storage_ingot_status[ore] < material_thresholds[ore] && ore_status[ore] > 0) {
   low_wm_arc_ore = ore;
  } else if (arc && high_wm_arc_ore == null && storage_ingot_status[ore] < (material_thresholds[ore] * 5) && ore_status[ore] > 0) {
   high_wm_arc_ore = ore;
  }
  if (high_wm_ore != null && low_wm_ore != null && high_wm_arc_ore != null && low_wm_arc_ore != null) {
   break;
  }
 }
 // now, reorder ore in refineries
 if (high_wm_ore != null || low_wm_ore != null) {
  var ore = low_wm_ore != null ? low_wm_ore : high_wm_ore;
  List < IMyTerminalBlock > refineries;
  refineries = getRefineries();
  foreach (var refinery in refineries) {
   var inv = refinery.GetInventory(0);
   var items = inv.GetItems();
   for (int j = 0; j < items.Count; j++) {
    var cur = items[j].Content.SubtypeName;
    if (cur == SCRAP) {
     cur = IRON;
    }
    if (cur != ore) {
     continue;
    }
    pushFront(inv, j, items[j].Amount);
    break;
   }
  }
 }
 // reorder ore in arc furnaces
 if (high_wm_arc_ore != null || low_wm_arc_ore != null) {
  var ore = low_wm_arc_ore != null ? low_wm_arc_ore : high_wm_arc_ore;
  List < IMyTerminalBlock > refineries;
  refineries = getArcFurnaces();
  foreach (var refinery in refineries) {
   var inv = refinery.GetInventory(0);
   var items = inv.GetItems();
   for (int j = 0; j < items.Count; j++) {
    var cur = items[j].Content.SubtypeName;
    if (cur == SCRAP) {
     cur = IRON;
    }
    if (cur != ore) {
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
public class RebalanceResult {
 public int minIndex;
 public int maxIndex;
 public Decimal minLoad;
 public Decimal maxLoad;
 public int minArcIndex;
 public int maxArcIndex;
 public Decimal minArcLoad;
 public Decimal maxArcLoad;
}

// go through a list of blocks and find most and least utilized
RebalanceResult findMinMax(List < IMyTerminalBlock > blocks) {
 RebalanceResult r = new RebalanceResult();
 int minI = 0, maxI = 0, minAI = 0, maxAI = 0;
 Decimal minL = Decimal.MaxValue, maxL = 0, minAL = Decimal.MaxValue, maxAL = 0;

 for (int i = 0; i < blocks.Count; i++) {
  var block = blocks[i];
  var inv = block.GetInventory(0);
  rebalance(inv);
  Decimal arcload = 0M;
  var items = inv.GetItems();
  foreach (var item in items) {
   var name = item.Content.SubtypeName;
   if (name == SCRAP) {
    name = IRON;
   }
   if (arc_furnace_ores.Contains(name)) {
    arcload += (Decimal) item.Amount * VOLUME_ORE;
   }
  }
  Decimal load = (Decimal) inv.CurrentVolume * 1000M;

  if (load < minL) {
   minI = i;
   minL = load;
  }
  if (load > maxL) {
   maxI = i;
   maxL = load;
  }
  if (arcload < minAL) {
   minAI = i;
   minAL = arcload;
  }
  if (arcload > maxAL) {
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
bool spreadOre(IMyTerminalBlock src, int srcIdx, IMyTerminalBlock dst, int dstIdx) {
 bool success = false;

 var src_inv = src.GetInventory(srcIdx);
 var dst_inv = dst.GetInventory(dstIdx);
 var maxLoad = (Decimal) src_inv.CurrentVolume * 1000M;
 var minLoad = (Decimal) dst_inv.CurrentVolume * 1000M;

 var items = src_inv.GetItems();
 var target_volume = (Decimal)(maxLoad - minLoad) / 2M;
 // spread all ore equally
 for (int i = items.Count - 1; i >= 0; i--) {
  var volume = items[i].Content.SubtypeName == SCRAP ? VOLUME_SCRAP : VOLUME_ORE;
  var cur_amount = (Decimal) items[i].Amount;
  var cur_vol = (cur_amount * volume) / 2M;
  Decimal amount = Math.Min(target_volume, cur_vol) / volume;
  VRage.MyFixedPoint ? tmp = (VRage.MyFixedPoint) amount;
  // if there's peanuts, send it all
  if (cur_amount < 250M) {
   tmp = null;
   amount = (Decimal) items[i].Amount;
  }
  amount = TryTransfer(src, srcIdx, dst, dstIdx, i, null, true, tmp);
  if (amount > 0) {
   success = true;
   target_volume -= amount * volume;
   if (target_volume <= 0) {
    break;
   }
  }
 }
 if (success) {
  rebalance(src_inv);
  rebalance(dst_inv);
 }
 return success;
}

// go through all refineries, arc furnaces and oxygen generators, and find
// least and most utilized, and spread load between them
void rebalanceRefineries() {
 bool refsuccess = false;
 bool arcsuccess = false;
 var ratio = 1.25M;

 // balance oxygen generators
 var ogs = getOxygenGenerators();
 RebalanceResult oxyresult = findMinMax(ogs);

 if (oxyresult.maxLoad > 0) {
  bool trySpread = oxyresult.minLoad == 0 || oxyresult.maxLoad / oxyresult.minLoad > ratio;
  if (oxyresult.minIndex != oxyresult.maxIndex && trySpread) {
   var src = ogs[oxyresult.maxIndex];
   var dst = ogs[oxyresult.minIndex];
   spreadOre(src, 0, dst, 0);
  }
 }

 // balance refineries and arc furnaces separately
 var refineries = getRefineries();
 var furnaces = getArcFurnaces();
 RebalanceResult refresult = findMinMax(refineries);
 RebalanceResult arcresult = findMinMax(furnaces);

 if (refresult.maxLoad > 250M) {
  bool trySpread = refresult.minLoad == 0 || refresult.maxLoad / refresult.minLoad > ratio;
  if (refresult.minIndex != refresult.maxIndex && trySpread) {
   var src = refineries[refresult.maxIndex];
   var dst = refineries[refresult.minIndex];
   if (spreadOre(src, 0, dst, 0)) {
    refsuccess = true;
   }
  }
 }
 if (arcresult.maxLoad > 250M) {
  bool trySpread = arcresult.minLoad == 0 || (arcresult.maxLoad / arcresult.minLoad) > ratio;
  if (arcresult.minIndex != arcresult.maxIndex && trySpread) {
   var src = furnaces[arcresult.maxIndex];
   var dst = furnaces[arcresult.minIndex];
   if (spreadOre(src, 0, dst, 0)) {
    arcsuccess = true;
   }
  }
 }

 if (refineries.Count == 0 || furnaces.Count == 0 || arcsuccess) {
  return;
 }

 // cross pollination: spread load from ref to arc
 Decimal refToArcRatio = 0;
 if (arcresult.minLoad != 0) {
  refToArcRatio = refresult.maxArcLoad / arcresult.minLoad;
 }
 bool refToArc = !refsuccess || (refsuccess && refresult.maxIndex != refresult.maxArcIndex);
 refToArc = refToArcRatio > ratio || (arcresult.minLoad == 0 && refresult.maxArcLoad > 0);
 if (refToArc) {
  var src = refineries[refresult.maxArcIndex];
  var dst = furnaces[arcresult.minIndex];
  if (spreadOre(src, 0, dst, 0)) {
   return;
  }
 }

 // spread load from arc to ref
 Decimal arcToRefRatio = 0;
 if (refresult.minLoad != 0) {
  arcToRefRatio = arcresult.maxLoad / refresult.minLoad;
 }

 bool arcToRef = refresult.minLoad == 0 || arcToRefRatio > ratio;
 if (arcToRef) {
  var src = furnaces[arcresult.maxIndex];
  var dst = refineries[refresult.minIndex];
  spreadOre(src, 0, dst, 0);
 }
}

// find ore of which we have the most amount of
string getBiggestOre() {
 Decimal max = 0;
 string name = "";
 foreach (var ore in ore_types) {
   // skip uranium
   if (ore == URANIUM) {
    continue;
   }
   var amount = ore_status[ore];
   if (amount > max) {
    name = ore;
    max = amount;
   }
  }
  // if we're out of ore
 if (max == 0) {
  return null;
 }
 return name;
}

// check if refineries can refine
bool refineriesClogged() {
 var refineries = getAllRefineries();
 foreach (IMyRefinery refinery in refineries) {
  if (refinery.IsQueueEmpty || refinery.IsProducing) {
   return false;
  }
 }
 return true;
}

/**
 * Declog and spread load
 */
void declogAssemblers() {
 var assemblers = getAssemblers();
 foreach (IMyAssembler assembler in assemblers) {
  var inv = assembler.GetInventory(0);

  // empty assembler input if it's not doing anything
  var items = inv.GetItems();
  if (assembler.IsQueueEmpty) {
   items = inv.GetItems();
   for (int j = items.Count - 1; j >= 0; j--) {
    pushToStorage(assembler, 0, j, null);
   }
  }

  inv = assembler.GetInventory(1);

  // empty output but only if it's not disassembling
  if (!assembler.DisassembleEnabled) {
   items = inv.GetItems();
   for (int j = items.Count - 1; j >= 0; j--) {
    pushToStorage(assembler, 1, j, null);
   }
  }
 }
}

void declogRefineries() {
 var refineries = getAllRefineries();
 foreach (var refinery in refineries) {
  var inv = refinery.GetInventory(1);
  var items = inv.GetItems();
  for (int j = items.Count - 1; j >= 0; j--) {
   pushToStorage(refinery, 1, j, null);
  }
 }
}

// find least and most utilized block, and spread load between them (used for
// drills, grinders and welders)
void spreadLoad(List < IMyTerminalBlock > blocks) {
 Decimal minLoad = 100, maxLoad = 0;
 Decimal minVol = 0, maxVol = 0;
 int minIndex = 0, maxIndex = 0;
 for (int i = 0; i < blocks.Count; i++) {
  Decimal load = (Decimal) blocks[i].GetInventory(0).CurrentVolume / (Decimal) blocks[i].GetInventory(0).MaxVolume;
  if (load < minLoad) {
   minIndex = i;
   minLoad = load;
   minVol = (Decimal) blocks[i].GetInventory(0).CurrentVolume * 1000M;
  }
  if (load > maxLoad) {
   maxIndex = i;
   maxLoad = load;
   maxVol = (Decimal) blocks[i].GetInventory(0).CurrentVolume * 1000M;
  }
 }
 // even out the load between biggest loaded block
 if (minIndex != maxIndex && (minLoad == 0 || maxLoad / minLoad > 1.1M)) {
  var src = blocks[maxIndex];
  var dst = blocks[minIndex];
  var src_inv = blocks[maxIndex].GetInventory(0);
  var dst_inv = blocks[minIndex].GetInventory(0);
  var target_volume = (maxVol - minVol) / 2M;
  var items = src_inv.GetItems();
  for (int i = items.Count - 1; i >= 0; i--) {
   if (target_volume <= 0) {
    return;
   }
   Decimal amount = (Decimal) items[i].Amount - 1M;
   // if it's peanuts, just send out everything
   if (amount < 1M) {
    src_inv.TransferItemTo(dst_inv, i, null, true, null);
    continue;
   }

   // send one and check load
   Decimal cur_vol = (Decimal) dst_inv.CurrentVolume * 1000M;
   if (!Transfer(src, 0, dst, 0, i, null, true, (VRage.MyFixedPoint) 1)) {
    continue;
   }
   Decimal new_vol = (Decimal) dst_inv.CurrentVolume * 1000M;
   Decimal item_vol = new_vol - cur_vol;
   int target_amount = (int) Math.Floor(target_volume / item_vol);
   src_inv.TransferItemTo(dst_inv, i, null, true, (VRage.MyFixedPoint) target_amount);
   target_volume -= Math.Min(target_amount, amount) * item_vol;
  }
 }
}

/**
 * Oxygen
 */
void checkOxygenLeaks() {
 var blocks = getAirVents();
 bool alert = false;
 foreach (IMyAirVent vent in blocks) {
  if (!vent.IsDepressurizing && !vent.CanPressurize) {
   addBlockAlert(vent, ALERT_OXYGEN_LEAK);
   alert = true;
  } else {
   removeBlockAlert(vent, ALERT_OXYGEN_LEAK);
  }
  displayBlockAlerts(vent);
 }
 if (alert) {
  addAlert(BROWN_ALERT);
 } else {
  removeAlert(BROWN_ALERT);
 }
}

void toggleOxygenGenerators(bool val) {
 var refineries = getOxygenGenerators();
 foreach (IMyOxygenGenerator refinery in refineries) {
  refinery.RequestEnable(val);
 }
}

bool iceAboveHighWatermark() {
 bool result = true;
 if (has_oxygen_tanks && oxygen_high_watermark > 0 && cur_oxygen_level < oxygen_high_watermark) {
  result = false;
 }
 if (has_hydrogen_tanks && hydrogen_high_watermark > 0 && cur_hydrogen_level < hydrogen_high_watermark) {
  result = false;
 }
 return result;
}

Decimal getStoredOxygen() {
 if (!can_refine_ice) {
  return 0M;
 }
 int n_oxygen_tanks = getOxygenTanks().Count;
 Decimal capacity = large_grid ? 100000M : 50000M;
 Decimal total_capacity = n_oxygen_tanks * capacity;
 Decimal ice_to_oxygen_ratio = 9M;
 Decimal stored_oxygen = ore_status[ICE] * ice_to_oxygen_ratio;
 return stored_oxygen / total_capacity * 100M;
}

Decimal getStoredHydrogen() {
 if (!can_refine_ice) {
  return 0M;
 }
 int n_hydrogen_tanks = getHydrogenTanks().Count;
 Decimal capacity = large_grid ? 2500000M : 40000M;
 Decimal total_capacity = n_hydrogen_tanks * capacity;
 Decimal ice_to_hydrogen_ratio = large_grid ? 9M : 4M;
 Decimal stored_hydrogen = ore_status[ICE] * ice_to_hydrogen_ratio;
 return stored_hydrogen / total_capacity * 100M;
}

/**
 * Functions pertaining to BARABAS's operation
 */
bool isShipMode() {
 return (op_mode & OP_MODE_SHIP) != 0;
}

bool isGenericShipMode() {
 return op_mode == OP_MODE_SHIP;
}

// tug is not considered a specialized ship
bool isSpecializedShipMode() {
 return (op_mode & (OP_MODE_DRILL | OP_MODE_WELDER | OP_MODE_GRINDER)) != 0;
}

bool isBaseMode() {
 return op_mode == OP_MODE_BASE;
}

bool isDrillMode() {
 return (op_mode & OP_MODE_DRILL) != 0;
}

bool isWelderMode() {
 return (op_mode & OP_MODE_WELDER) != 0;
}

bool isGrinderMode() {
 return (op_mode & OP_MODE_GRINDER) != 0;
}

bool isTugMode() {
 return (op_mode & OP_MODE_TUG) != 0;
}

bool isAutoMode() {
 return op_mode == OP_MODE_AUTO;
}

void setMode(int mode) {
 switch (mode) {
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

void resetConfig() {
 hud_notifications = true;
 sort_storage = false;
 pull_ore_from_base = false;
 pull_ingots_from_base = false;
 pull_components_from_base = false;
 push_ore_to_base = false;
 push_ingots_to_base = false;
 push_components_to_base = false;
 throw_out_stone = true;
 material_thresholds[STONE] = 5000M;
 power_low_watermark = 0;
 power_high_watermark = 0;
 oxygen_high_watermark = 0;
 oxygen_low_watermark = 0;
 hydrogen_high_watermark = 0;
 hydrogen_low_watermark = 0;
}

// update defaults based on auto configured values
void autoConfigure() {
 resetConfig();
 if (isBaseMode()) {
  sort_storage = true;
 } else if (isDrillMode()) {
  push_ore_to_base = true;
  if (can_refine) {
   push_ingots_to_base = true;
  }
 } else if (isGrinderMode()) {
  push_components_to_base = true;
 }
}

void configureWatermarks() {
 if (isShipMode()) {
  if (!has_reactors) {
   auto_refuel_ship = false;
  } else {
   auto_refuel_ship = true;
  }
 }
 if (power_low_watermark == 0) {
  if (isBaseMode()) {
   power_low_watermark = 60;
  } else {
   power_low_watermark = 10;
  }
 }
 if (power_high_watermark == 0) {
  if (isBaseMode()) {
   power_high_watermark = 480;
  } else {
   power_high_watermark = 30;
  }
 }
 if (oxygen_high_watermark == 0 && has_oxygen_tanks) {
  if (isBaseMode()) {
   oxygen_high_watermark = 30;
  } else {
   oxygen_high_watermark = 60;
  }
 }
 if (oxygen_low_watermark == 0 && has_oxygen_tanks) {
  if (isBaseMode()) {
   oxygen_low_watermark = 10;
  } else {
   oxygen_low_watermark = 15;
  }
 }
 if (hydrogen_high_watermark == 0 && has_hydrogen_tanks) {
  hydrogen_high_watermark = 80;
 }
 if (hydrogen_low_watermark == 0 && has_hydrogen_tanks) {
  if (isBaseMode()) {
   hydrogen_low_watermark = 10;
  } else {
   hydrogen_low_watermark = 30;
  }
 }
}

// select operation mode
void selectOperationMode() {
 // if we found some thrusters or wheels, assume we're a ship
 if (local_grid_data.has_thrusters || local_grid_data.has_wheels) {
  // this is likely a drill ship
  if (local_grid_data.has_drills && !local_grid_data.has_welders && !local_grid_data.has_grinders) {
   setMode(OP_MODE_DRILL);
  }
  // this is likely a welder ship
  else if (local_grid_data.has_welders && !local_grid_data.has_drills && !local_grid_data.has_grinders) {
   setMode(OP_MODE_WELDER);
  }
  // this is likely a grinder ship
  else if (local_grid_data.has_grinders && !local_grid_data.has_drills && !local_grid_data.has_welders) {
   setMode(OP_MODE_GRINDER);
  }
  // we don't know what the hell this is, so don't adjust the defaults
  else {
   setMode(OP_MODE_SHIP);
  }
 } else {
  setMode(OP_MODE_BASE);
 }
}

void addAlert(int level) {
 var alert = text_alerts[level];
 // this alert is already enabled
 if (alert.enabled) {
  return;
 }
 alert.enabled = true;

 var sb = new StringBuilder();

 removeAntennaAlert(ALERT_LOW_POWER);
 removeAntennaAlert(ALERT_LOW_STORAGE);
 removeAntennaAlert(ALERT_VERY_LOW_STORAGE);
 removeAntennaAlert(ALERT_MATERIAL_SHORTAGE);

 alert = null;
 // now, find enabled alerts
 bool first = true;
 for (int i = 0; i < text_alerts.Count; i++) {
  if (text_alerts[i].enabled) {
   if (i == BLUE_ALERT) {
    addAntennaAlert(ALERT_LOW_POWER);
   } else if (i == YELLOW_ALERT) {
    addAntennaAlert(ALERT_LOW_STORAGE);
   } else if (i == RED_ALERT) {
    addAntennaAlert(ALERT_VERY_LOW_STORAGE);
   } else if (i == WHITE_ALERT) {
    addAntennaAlert(ALERT_MATERIAL_SHORTAGE);
   }
   if (alert == null) {
    alert = text_alerts[i];
   }
   if (!first) {
    sb.Append(", ");
   }
   first = false;
   sb.Append(alert.text);
  }
 }
 displayAntennaAlerts();
 showAlertColor(alert.color);
 status_report[STATUS_ALERT] = sb.ToString();
}

void removeAlert(int level) {
 // disable the alert
 var alert = text_alerts[level];
 alert.enabled = false;

 // now, see if we should display another alert
 var sb = new StringBuilder();
 alert = null;

 removeAntennaAlert(ALERT_LOW_POWER);
 removeAntennaAlert(ALERT_LOW_STORAGE);
 removeAntennaAlert(ALERT_VERY_LOW_STORAGE);
 removeAntennaAlert(ALERT_MATERIAL_SHORTAGE);

 // now, find enabled alerts
 bool first = true;
 for (int i = 0; i < text_alerts.Count; i++) {
  if (text_alerts[i].enabled) {
   if (i == BLUE_ALERT) {
    addAntennaAlert(ALERT_LOW_POWER);
   } else if (i == YELLOW_ALERT) {
    addAntennaAlert(ALERT_LOW_STORAGE);
   } else if (i == RED_ALERT) {
    addAntennaAlert(ALERT_VERY_LOW_STORAGE);
   } else if (i == WHITE_ALERT) {
    addAntennaAlert(ALERT_MATERIAL_SHORTAGE);
   }
   if (alert == null) {
    alert = text_alerts[i];
   }
   if (!first) {
    sb.Append(", ");
   }
   first = false;
   sb.Append(alert.text);
  }
 }
 status_report[STATUS_ALERT] = sb.ToString();
 if (alert == null) {
  hideAlertColor();
 } else {
  showAlertColor(alert.color);
 }
 displayAntennaAlerts();
}

bool clStrCompare(string str1, string str2) {
 return String.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
}

string getWatermarkStr(Decimal low, Decimal high) {
 return String.Format("{0:0} {1:0}", low, high);
}

bool parseWatermarkStr(string val, out Decimal low, out Decimal high) {
 low = 0;
 high = 0;

 // trim all multiple spaces
 var opt = System.Text.RegularExpressions.RegexOptions.None;
 var regex = new System.Text.RegularExpressions.Regex("\\s{2,}", opt);
 val = regex.Replace(val, " ");

 string[] strs = val.Split(' ');
 if (strs.Length != 2) {
  return false;
 }
 if (!Decimal.TryParse(strs[0], out low)) {
  return false;
 }
 if (!Decimal.TryParse(strs[1], out high)) {
  return false;
 }
 return true;
}

string generateConfiguration() {
 var sb = new StringBuilder();

 if (isBaseMode()) {
  config_options[CONFIGSTR_OP_MODE] = "base";
 } else if (isGenericShipMode()) {
  config_options[CONFIGSTR_OP_MODE] = "ship";
 } else if (isDrillMode()) {
  config_options[CONFIGSTR_OP_MODE] = "drill";
 } else if (isWelderMode()) {
  config_options[CONFIGSTR_OP_MODE] = "welder";
 } else if (isGrinderMode()) {
  config_options[CONFIGSTR_OP_MODE] = "grinder";
 } else if (isTugMode()) {
  config_options[CONFIGSTR_OP_MODE] = "tug";
 }
 config_options[CONFIGSTR_HUD_NOTIFICATIONS] = Convert.ToString(hud_notifications);
 config_options[CONFIGSTR_POWER_WATERMARKS] = getWatermarkStr(power_low_watermark, power_high_watermark);
 config_options[CONFIGSTR_PUSH_ORE] = Convert.ToString(push_ore_to_base);
 config_options[CONFIGSTR_PUSH_INGOTS] = Convert.ToString(push_ingots_to_base);
 config_options[CONFIGSTR_PUSH_COMPONENTS] = Convert.ToString(push_components_to_base);
 config_options[CONFIGSTR_PULL_ORE] = Convert.ToString(pull_ore_from_base);
 config_options[CONFIGSTR_PULL_INGOTS] = Convert.ToString(pull_ingots_from_base);
 config_options[CONFIGSTR_PULL_COMPONENTS] = Convert.ToString(pull_components_from_base);
 config_options[CONFIGSTR_SORT_STORAGE] = Convert.ToString(sort_storage);
 if (throw_out_stone) {
  if (material_thresholds[STONE] == 0) {
   config_options[CONFIGSTR_KEEP_STONE] = "none";
  } else {
   config_options[CONFIGSTR_KEEP_STONE] = Convert.ToString(Math.Floor((material_thresholds[STONE] * 5) / 1000));
  }
 } else {
  config_options[CONFIGSTR_KEEP_STONE] = "all";
 }
 if (oxygen_low_watermark > 0) {
  config_options[CONFIGSTR_OXYGEN_WATERMARKS] = getWatermarkStr(oxygen_low_watermark, oxygen_high_watermark);
 } else {
  config_options[CONFIGSTR_OXYGEN_WATERMARKS] = "none";
 }
 if (hydrogen_low_watermark > 0) {
  config_options[CONFIGSTR_HYDROGEN_WATERMARKS] = getWatermarkStr(hydrogen_low_watermark, hydrogen_high_watermark);
 } else {
  config_options[CONFIGSTR_HYDROGEN_WATERMARKS] = "none";
 }

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
 sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 1 and 100.");
 sb.AppendLine("# First number is when to sound an alarm.");
 sb.AppendLine("# Second number is when to stop refining ice.");
 sb.AppendLine(key + " = " + config_options[key]);
 sb.AppendLine();
 key = CONFIGSTR_HYDROGEN_WATERMARKS;
 sb.AppendLine("# Percentage of hydrogen to keep.");
 sb.AppendLine("# Can be \"none\", \"auto\", or two numbers between 1 and 100.");
 sb.AppendLine("# First number is when to sound an alarm.");
 sb.AppendLine("# Second number is when to stop refining ice.");
 sb.AppendLine(key + " = " + config_options[key]);
 sb.AppendLine();
 key = CONFIGSTR_KEEP_STONE;
 sb.AppendLine("# How much stone to keep, in tons.");
 sb.AppendLine("# Can be a positive number, \"none\", \"all\" or \"auto\".");
 sb.AppendLine(key + " = " + config_options[key]);
 sb.AppendLine();
 key = CONFIGSTR_SORT_STORAGE;
 sb.AppendLine("# Automatically sort items in storage containers.");
 sb.AppendLine("# Can be True or False.");
 sb.AppendLine(key + " = " + config_options[key]);
 sb.AppendLine();
 sb.AppendLine("#");
 sb.AppendLine("# Values below this line are only applicable to");
 sb.AppendLine("# ships when connected to base or other ships.");
 sb.AppendLine("#");
 sb.AppendLine();
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

 return sb.ToString();
}

void rebuildConfiguration() {
 var block = getConfigBlock();
 if (block == null) {
  return;
 }

 // generate config
 string text = generateConfiguration();

 // put text back into the text block
 block.WritePrivateText(text);
 block.WritePublicText("   BARABAS Configuration");
 block.WritePrivateTitle("BARABAS Configuration");
 block.ShowPublicTextOnScreen();
}

void parseLine(string line) {
 string[] strs = line.Split('=');
 if (strs.Length != 2) {
  throw new BarabasException("Invalid number of tokens: " + line, this);
 }
 var str = strs[0].ToLower().Trim();
 var strval = strs[1].ToLower().Trim();
 // backwards compatibility
 if (str == "ingots per reactor" || str == "chunk size") {
  return;
 }
 if (str == "throw out stone") {
  if (strval == "False") {
   throw_out_stone = false;
   material_thresholds[STONE] = 0;
  } else {
   throw_out_stone = true;
   material_thresholds[STONE] = 5000M;
  }
  return;
 }
 if (str == "reactor low watermark" || str == "power low watermark") {
  if (Decimal.TryParse(strval, out power_low_watermark)) {
   return;
  } else {
   throw new BarabasException("Invalid config value: " + strval, this);
  }
 }
 if (str == "reactor high watermark" || str == "power high watermark") {
  if (Decimal.TryParse(strval, out power_high_watermark)) {
   return;
  } else {
   throw new BarabasException("Invalid config value: " + strval, this);
  }
 }
 if (str == "oxygen threshold") {
  Decimal tmp;
  if (strval == "none") {
   oxygen_low_watermark = -1;
   oxygen_high_watermark = -1;
   return;
  } else if (Decimal.TryParse(strval, out tmp) && tmp > 0 && tmp <= 100) {
   oxygen_low_watermark = tmp;
   oxygen_high_watermark = Math.Min(tmp * 2, 100);
   return;
  } else {
   throw new BarabasException("Invalid config value: " + strval, this);
  }
 }
 if (str == "hydrogen threshold") {
  Decimal tmp;
  if (strval == "none") {
   hydrogen_low_watermark = -1;
   hydrogen_high_watermark = -1;
   return;
  } else if (Decimal.TryParse(strval, out tmp) && tmp > 0 && tmp <= 100) {
   hydrogen_low_watermark = tmp;
   hydrogen_high_watermark = Math.Min(tmp * 2, 100);
   return;
  } else {
   throw new BarabasException("Invalid config value: " + strval, this);
  }
 }
 if (!config_options.ContainsKey(str)) {
  throw new BarabasException("Invalid config option: " + str, this);
 }
 // now, try to parse it
 bool fail = false;
 bool bval, bparse, fparse;
 Decimal fval;
 bparse = Boolean.TryParse(strval, out bval);
 fparse = Decimal.TryParse(strval, out fval);
 // op mode
 if (clStrCompare(str, CONFIGSTR_OP_MODE)) {
  if (strval == "base") {
   if (!isBaseMode()) {
    setMode(OP_MODE_BASE);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "ship") {
   if (!isGenericShipMode()) {
    setMode(OP_MODE_SHIP);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "drill") {
   if (!isDrillMode()) {
    setMode(OP_MODE_DRILL);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "welder") {
   if (!isWelderMode()) {
    setMode(OP_MODE_WELDER);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "grinder") {
   if (!isGrinderMode()) {
    setMode(OP_MODE_GRINDER);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "tug") {
   if (!isTugMode()) {
    setMode(OP_MODE_TUG);
    crisis_mode = CRISIS_MODE_NONE;
   }
  } else if (strval == "auto") {
   setMode(OP_MODE_AUTO);
   crisis_mode = CRISIS_MODE_NONE;
  } else {
   fail = true;
  }
 } else if (clStrCompare(str, CONFIGSTR_POWER_WATERMARKS)) {
  Decimal low, high;
  if (strval == "auto") {
   power_low_watermark = 0;
   power_high_watermark = 0;
  } else if (parseWatermarkStr(strval, out low, out high)) {
   if (low > 0) {
    power_low_watermark = low;
   } else {
    fail = true;
   }
   if (high > 0 && low <= high) {
    power_high_watermark = high;
   } else {
    fail = true;
   }
  } else {
   fail = true;
  }
 } else if (clStrCompare(str, CONFIGSTR_KEEP_STONE)) {
  if (fparse && fval > 0) {
   throw_out_stone = true;
   material_thresholds[STONE] = (Decimal) Math.Floor((fval * 1000M) / 5);
  } else if (strval == "all") {
   throw_out_stone = false;
   material_thresholds[STONE] = 5000M;
  } else if (strval == "none") {
   throw_out_stone = true;
   material_thresholds[STONE] = 0;
  } else if (strval == "auto") {
   throw_out_stone = true;
   material_thresholds[STONE] = 5000M;
  } else {
   fail = true;
  }
 } else if (clStrCompare(str, CONFIGSTR_OXYGEN_WATERMARKS)) {
  Decimal low, high;
  if (strval == "auto") {
   oxygen_low_watermark = 0;
   oxygen_high_watermark = 0;
  } else if (strval == "none") {
   oxygen_low_watermark = -1;
   oxygen_high_watermark = -1;
  } else if (parseWatermarkStr(strval, out low, out high)) {
   if (low > 0 && low <= 100) {
    oxygen_low_watermark = low;
   } else {
    fail = true;
   }
   if (high > 0 && high <= 100 && low <= high) {
    oxygen_high_watermark = high;
   } else {
    fail = true;
   }
  } else {
   fail = true;
  }
 } else if (clStrCompare(str, CONFIGSTR_HYDROGEN_WATERMARKS)) {
  Decimal low, high;
  if (strval == "auto") {
   hydrogen_low_watermark = 0;
   hydrogen_high_watermark = 0;
  } else if (strval == "none") {
   hydrogen_low_watermark = -1;
   hydrogen_high_watermark = -1;
  } else if (parseWatermarkStr(strval, out low, out high)) {
   if (low > 0 && low <= 100) {
    hydrogen_low_watermark = low;
   } else {
    fail = true;
   }
   if (high > 0 && high <= 100 && low <= high) {
    hydrogen_high_watermark = high;
   } else {
    fail = true;
   }
  } else {
   fail = true;
  }
 }
 // bools
 else if (bparse) {
  if (clStrCompare(str, CONFIGSTR_PUSH_ORE)) {
   push_ore_to_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_PUSH_INGOTS)) {
   push_ingots_to_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_PUSH_COMPONENTS)) {
   push_components_to_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_PULL_ORE)) {
   pull_ore_from_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_PULL_INGOTS)) {
   pull_ingots_from_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_PULL_COMPONENTS)) {
   pull_components_from_base = bval;
  } else if (clStrCompare(str, CONFIGSTR_SORT_STORAGE)) {
   sort_storage = bval;
  } else if (clStrCompare(str, CONFIGSTR_HUD_NOTIFICATIONS)) {
   hud_notifications = bval;
  }
 } else {
  fail = true;
 }
 if (fail) {
  throw new BarabasException("Invalid config value: " + strval, this);
 }
}

// this will find a BARABAS Config block and read its configuration
void parseConfiguration() {
 // find the block, blah blah
 var block = getConfigBlock();
 if (block == null) {
  return;
 }
 string text;

 // update from older versions, move config to private text
 if (block.GetPublicTitle() == "BARABAS Configuration") {
  text = block.GetPublicText();
  block.WritePublicText("");
  block.WritePublicTitle("");
 } else {
  text = block.GetPrivateText();
 }

 // check if the text is empty
 if (text.Trim().Length != 0) {
  var lines = text.Split('\n');
  foreach (var l in lines) {
   var line = l.Trim();

   // skip comments and empty lines
   if (line.StartsWith("#") || line.Length == 0) {
    continue;
   }
   parseLine(line);
  }
 }
}

string getBlockAlerts(int ids) {
 var sb = new StringBuilder();
 int idx = 0;
 bool first = true;
 while (ids != 0) {
  if ((ids & 0x1) != 0) {
   if (!first) {
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

void displayBlockAlerts(IMyTerminalBlock block) {
 if (!hud_notifications) {
  return;
 }
 var name = getBlockName(block);
 if (!blocks_to_alerts.ContainsKey(block)) {
  setBlockName(block, name, "");
  hideFromHud(block);
  return;
 }
 var cur = blocks_to_alerts[block];
 var alerts = getBlockAlerts(cur);
 setBlockName(block, name, alerts);
 showOnHud(block);
}

void addBlockAlert(IMyTerminalBlock block, int id) {
 if (!hud_notifications) {
  return;
 }
 if (blocks_to_alerts.ContainsKey(block)) {
  blocks_to_alerts[block] |= id;
 } else {
  blocks_to_alerts.Add(block, id);
 }
}

void removeBlockAlert(IMyTerminalBlock block, int id) {
 if (!hud_notifications) {
  return;
 }
 if (blocks_to_alerts.ContainsKey(block)) {
  var cur = blocks_to_alerts[block];
  cur &= ~id;
  if (cur != 0) {
   blocks_to_alerts[block] = cur;
  } else {
   blocks_to_alerts.Remove(block);
  }
 }
}

string getBlockName(IMyTerminalBlock block) {
 var name = block.CustomName;
 var regex = new System.Text.RegularExpressions.Regex("\\[BARABAS");
 var match = regex.Match(name);
 if (!match.Success) {
  return name;
 }
 return name.Substring(0, match.Index - 1);
}

void setBlockName(IMyTerminalBlock antenna, string name, string alert) {
 if (alert == "") {
  antenna.SetCustomName(name);
 } else {
  antenna.SetCustomName(name + " [BARABAS: " + alert + "]");
 }
}

void showOnHud(IMyTerminalBlock block) {
 if (block.GetProperty("ShowOnHUD") != null) {
  block.SetValue("ShowOnHUD", true);
 }
}

void hideFromHud(IMyTerminalBlock block) {
 if (block.GetProperty("ShowOnHUD") != null) {
  block.SetValue("ShowOnHUD", false);
 }
}

void displayAntennaAlerts() {
 if (!hud_notifications) {
  return;
 }
 var antennas = getAntennas();
 foreach (var antenna in antennas) {
  displayBlockAlerts(antenna);
 }
}

void addAntennaAlert(int id) {
 if (!hud_notifications) {
  return;
 }
 var antennas = getAntennas();
 foreach (var antenna in antennas) {
  addBlockAlert(antenna, id);
 }
}

void removeAntennaAlert(int id) {
 if (!hud_notifications) {
  return;
 }
 var antennas = getAntennas();
 foreach (var antenna in antennas) {
  removeBlockAlert(antenna, id);
 }
}

void showAlertColor(Color c) {
 var lights = getLights();
 foreach (IMyLightingBlock light in lights) {
  if (light.GetValue < Color > ("Color").Equals(c) && light.Enabled) {
   continue;
  }
  light.SetValue("Color", c);
  // make sure we switch the color of the texture as well
  light.RequestEnable(false);
  light.RequestEnable(true);
 }
}

void hideAlertColor() {
 var lights = getLights();
 foreach (IMyLightingBlock light in lights) {
  if (!light.Enabled) {
   continue;
  }
  light.RequestEnable(false);
 }
}

void turnOffConveyors() {
 var blocks = getBlocks();
 // go through all blocks and set "use conveyor" to off
 foreach (var block in blocks) {
  if (block is IMyAssembler) {
   continue;
  }
  if (block is IMyShipWelder) {
   continue;
  }

  var prod = block as IMyProductionBlock;
  if (prod != null && prod.UseConveyorSystem) {
   block.ApplyAction("UseConveyor");
  }
 }
}

void displayStatusReport() {
 var panels = getTextPanels();

 removeAntennaAlert(ALERT_CRISIS_LOCKUP);
 removeAntennaAlert(ALERT_CRISIS_STANDBY);
 removeAntennaAlert(ALERT_CRISIS_THROW_OUT);

 if (crisis_mode == CRISIS_MODE_NONE && tried_throwing) {
  status_report[STATUS_CRISIS_MODE] = "Standby";
  addAntennaAlert(ALERT_CRISIS_STANDBY);
 } else if (crisis_mode == CRISIS_MODE_NONE) {
  status_report[STATUS_CRISIS_MODE] = "";
 } else if (crisis_mode == CRISIS_MODE_THROW_ORE) {
  status_report[STATUS_CRISIS_MODE] = "Ore throwout";
  addAntennaAlert(ALERT_CRISIS_THROW_OUT);
 } else if (crisis_mode == CRISIS_MODE_LOCKUP) {
  status_report[STATUS_CRISIS_MODE] = "Lockup";
  addAntennaAlert(ALERT_CRISIS_LOCKUP);
 }

 displayAntennaAlerts();

 // construct panel text
 var sb = new StringBuilder();
 foreach (var pair in status_report) {
  if (pair.Value == "") {
   continue;
  }
  sb.AppendLine(String.Format("{0}: {1}", pair.Key, pair.Value));
 }
 foreach (IMyTextPanel panel in panels) {
  panel.WritePublicText(sb.ToString());
  panel.WritePublicTitle("BARABAS Notify Report");
  panel.ShowTextureOnScreen();
  panel.ShowPublicTextOnScreen();
 }
}

/*
 * States
 */

void s_refreshGrids() {
 getLocalGrids(true);
 getBlocks(true);
}

void s_refreshProduction() {
 has_refineries = getRefineries(true).Count > 0;
 has_arc_furnaces = getArcFurnaces(true).Count > 0;
 can_refine = has_refineries || has_arc_furnaces || (getOxygenGenerators(true).Count > 0);
 can_refine_ice = has_refineries || (getOxygenGenerators().Count > 0);
 can_use_ingots = getAssemblers(true).Count > 0;
 getStorage(true);
 turnOffConveyors();
}

void s_refreshPower() {
 has_reactors = getReactors(true).Count > 0;
 getBatteries(true);
 if (has_reactors) {
  getMaxReactorPowerOutput(true);
  getCurReactorPowerOutput(true);
 }
 getMaxBatteryPowerOutput(true);
 getCurPowerDraw(true);
 getMaxPowerDraw(true);
}

void s_refreshOxyHydro() {
 has_air_vents = getAirVents(true).Count > 0;
 has_oxygen_tanks = getOxygenTanks(true).Count > 0;
 has_hydrogen_tanks = getHydrogenTanks(true).Count > 0;
 can_use_oxygen = has_oxygen_tanks && has_air_vents;
}

void s_refreshTools() {
 has_drills = getDrills(true).Count > 0;
 has_grinders = getGrinders(true).Count > 0;
 has_welders = getWelders(true).Count > 0;
}

void s_refreshMisc() {
 has_connectors = getConnectors(true).Count > 0;
 has_status_panels = getTextPanels(true).Count > 0;
 spreadLoad(getTrashConnectors(true));
 getTrashSensors(true);
 getLights(true);
 getAntennas(true);
 startThrowing();
}

void s_refreshConfig() {
 // configure BARABAS
 getConfigBlock(true);
 parseConfiguration();
 if (isAutoMode()) {
  selectOperationMode();
  autoConfigure();
 }
 if (isShipMode()) {
  Me.SetCustomName("BARABAS Ship CPU");
 } else {
  Me.SetCustomName("BARABAS Base CPU");
 }
 configureWatermarks();
 rebuildConfiguration();

 if (pull_ingots_from_base && push_ingots_to_base) {
  throw new BarabasException("Invalid configuration - " +
   "pull_ingots_from_base and push_ingots_to_base both set to \"true\"", this);
 }
 if (pull_ore_from_base && push_ore_to_base) {
  throw new BarabasException("Invalid configuration - " +
   "pull_ore_from_base and push_ore_to_base both set to \"true\"", this);
 }
 if (pull_components_from_base && push_components_to_base) {
  throw new BarabasException("Invalid configuration - " +
   "pull_components_from_base and push_components_to_base both set to \"true\"", this);
 }
}

void s_refreshRemote() {
 // local refresh finished, now see if we have any remote grids
 findRemoteGrids();
 // signal that we have something connected to us
 if (connected) {
  getRemoteStorage(true);
  getRemoteShipStorage(true);
  addAlert(GREEN_ALERT);
 } else {
  removeAlert(GREEN_ALERT);
 }
}

void s_power() {
 // determine if we need more uranium
 bool above_high_watermark = powerAboveHighWatermark();
 var max_pwr_output = getCurReactorPowerOutput() + getMaxBatteryPowerOutput();

 // if we have enough uranium ingots, business as usual
 if (!above_high_watermark) {
  // check if we're below low watermark
  bool above_low_watermark = powerAboveLowWatermark();

  if (has_reactors && has_refineries && ore_status[URANIUM] > 0) {
   prioritize_uranium = true;
  }

  if (!above_low_watermark && max_pwr_output != 0) {
   addAlert(BLUE_ALERT);
  } else {
   removeAlert(BLUE_ALERT);
  }
 } else {
  removeAlert(BLUE_ALERT);
  prioritize_uranium = false;
 }
 status_report[STATUS_POWER_STATS] = "No power sources";

 if (max_pwr_output != 0) {
  // figure out how much time we have on batteries and reactors
  Decimal stored_power = getBatteryStoredPower() + getReactorStoredPower();
  // prevent division by zero
  var max_pwr_draw = Math.Max(getMaxPowerDraw(), 0.001M);
  var cur_pwr_draw = Math.Max(getCurPowerDraw(), 0.001M);
  var adjusted_pwr_draw = (cur_pwr_draw + prev_pwr_draw) / 2M;
  prev_pwr_draw = cur_pwr_draw;
  Decimal time;
  string time_str;
  if (isShipMode() && connected_to_base) {
   time = Math.Round(stored_power / max_pwr_draw, 0);
  } else {
   time = Math.Round(stored_power / adjusted_pwr_draw, 0);
  }
  if (time > 300) {
   time = Math.Floor(time / 60M);
   if (time > 48) {
    time = Math.Floor(time / 24M);
    if (time > 30) {
     time_str = "lots";
    } else {
     time_str = Convert.ToString(time) + " d";
    }
   } else {
    time_str = Convert.ToString(time) + " h";
   }
  } else {
   time_str = Convert.ToString(time) + " m";
  }
  string max_str = String.Format("{0:0.0}%", max_pwr_draw / max_pwr_output * 100);
  string cur_str = String.Format("{0:0.0}%", adjusted_pwr_draw / max_pwr_output * 100);
  status_report[STATUS_POWER_STATS] = String.Format("{0}/{1}/{2}", max_str, cur_str, time_str);
 }

 if (crisis_mode == CRISIS_MODE_NONE) {
  bool can_refuel = isShipMode() && connected_to_base;
  if (refillReactors()) {
   pushSpareUraniumToStorage();
  }
 }
 // if we're in a crisis, push all available uranium ingots to reactors.
 else {
  refillReactors(true);
 }
}

void s_refineries() {
 if (crisis_mode == CRISIS_MODE_NONE && can_refine) {
  // if we're a ship and we're connected, push ore to storage
  if (isShipMode() && push_ore_to_base && connected_to_base) {
   pushOreToStorage();
  } else {
   refineOre();
  }
 }
}

void s_processIce() {
 toggleOxygenGenerators(refine_ice);
 if (refine_ice) {
  return;
 }
 pushIceToStorage();
}

void s_materialsPriority() {
 // check if any ore needs to be prioritized
 if (can_use_ingots || prioritize_uranium) {
  reprioritizeOre();
 }
}

void s_materialsRebalance() {
 if (can_refine) {
  rebalanceRefineries();
 }
}

void s_materialsCrisis() {
 if (crisis_mode == CRISIS_MODE_NONE && throw_out_stone) {
  // check if we want to throw out extra stone
  if (storage_ore_status[STONE] > 0) {
   Decimal excessStone = storage_ingot_status[STONE] + storage_ore_status[STONE] * ore_to_ingot_ratios[STONE] - material_thresholds[STONE] * 5;
   excessStone = Math.Min(excessStone / ore_to_ingot_ratios[STONE], storage_ore_status[STONE]);
   if (excessStone > 0) {
    throwOutOre(STONE, excessStone);
   } else {
    storeTrash();
   }
  }
 } else if (crisis_mode == CRISIS_MODE_THROW_ORE) {
  tried_throwing = true;
  // if we can't even throw out ore, well, all bets are off
  string ore = getBiggestOre();
  if ((ore != null && !throwOutOre(ore, 0, true)) || ore == null) {
   storeTrash(true);
   crisis_mode = CRISIS_MODE_LOCKUP;
  }
 }
}

void s_toolsDrills() {
 if (has_drills) {
  var drills = getDrills();
  emptyBlocks(drills);
  spreadLoad(drills);
 }
}

void s_toolsGrinders() {
 if (has_grinders) {
  var grinders = getGrinders();
  emptyBlocks(grinders);
  spreadLoad(grinders);
 }
}

void s_toolsWelders() {
 if (has_welders && isWelderMode()) {
  fillWelders();
 }
}

void s_declogAssemblers() {
 if (can_use_ingots) {
  declogAssemblers();
 }
}

void s_declogRefineries() {
 if (can_refine) {
  declogRefineries();
 }
}

void s_localStorage() {
 if (sort_storage) {
  sortLocalStorage();
 }
}

void s_remoteStorage() {
 if (isShipMode() && connected_to_base) {
  pushAllToRemoteStorage();
  pullFromRemoteStorage();
 }
 // tug is a special case as it can push to and pull from ships, but only
 // when connected to a ship and not to a base
 else if (isTugMode() && connected_to_ship && !connected_to_base) {
  pullFromRemoteShipStorage();
  pushToRemoteShipStorage();
 }
}

string roundStr(Decimal val) {
 if (val >= 10000M) {
  return String.Format("{0}k", Math.Floor(val / 1000M));
 } else if (val >= 1000M) {
  return String.Format("{0:0.#}k", val / 1000M);
 } else if (val >= 100) {
  return String.Format("{0}", Math.Floor(val));
 } else {
  return String.Format("{0:0.#}", val);
 }
}

void s_updateMaterialStats() {
 Decimal uranium_in_reactors = 0;
 // clear stats
 foreach (var ore in ore_types) {
  ore_status[ore] = 0;
  storage_ore_status[ore] = 0;
  ingot_status[ore] = 0;
  storage_ingot_status[ore] = 0;
 }
 var blocks = getBlocks();
 foreach (var block in blocks) {
  for (int i = 0; i < block.GetInventoryCount(); i++) {
   var inv = block.GetInventory(i);
   var items = inv.GetItems();
   foreach (var item in items) {
    bool isStorage = block is IMyCargoContainer;
    bool isReactor = block is IMyReactor;
    if (isOre(item)) {
     string name = item.Content.SubtypeName;
     if (item.Content.SubtypeName == SCRAP) {
      name = IRON;
     }
     ore_status[name] += (Decimal) item.Amount;
     if (isStorage) {
      storage_ore_status[name] += (Decimal) item.Amount;
     }
    } else if (isIngot(item)) {
     string name = item.Content.SubtypeName;
     var amount = (Decimal) item.Amount;
     ingot_status[name] += amount;
     if (isStorage) {
      storage_ingot_status[name] += amount;
     }
     if (isReactor) {
      uranium_in_reactors += amount;
     }
    }
   }
  }
 }
 bool alert = false;
 var sb = new StringBuilder();
 foreach (var ore in ore_types) {
  Decimal total_ingots = 0;
  Decimal total_ore = ore_status[ore];
  Decimal total = 0;
  if (ore != ICE) {
   total_ingots = ingot_status[ore];
   if (ore == URANIUM) {
    total_ingots -= uranium_in_reactors;
   }
   total = (total_ore * ore_to_ingot_ratios[ore]) + total_ingots;
  } else {
   total = total_ore;
  }

  if (has_status_panels) {
   if (isShipMode() && total == 0) {
    continue;
   }
   sb.Append("\n  ");
   sb.Append(ore);
   sb.Append(": ");
   sb.Append(roundStr(total_ore));

   if (ore != ICE) {
    sb.Append(" / ");
    sb.Append(roundStr(total_ingots));
    if (total_ore > 0) {
     sb.Append(String.Format(" ({0})", roundStr(total)));
    }
   }
  }

  if (ore != ICE && can_use_ingots && total < material_thresholds[ore]) {
   alert = true;
   if (has_status_panels) {
    sb.Append(" WARNING!");
   }
  }
 }
 if (alert) {
  addAlert(WHITE_ALERT);
 } else {
  removeAlert(WHITE_ALERT);
 }
 alert = false;
 status_report[STATUS_MATERIAL] = sb.ToString();

 // display oxygen and hydrogen stats
 if (has_oxygen_tanks || has_hydrogen_tanks) {
  Decimal oxy_cur = 0, oxy_total = 0;
  Decimal hydro_cur = 0, hydro_total = 0;
  var tanks = getOxygenTanks();
  foreach (IMyOxygenTank tank in tanks) {
   oxy_cur += (Decimal) tank.GetOxygenLevel();
   oxy_total += 1M;
  }
  tanks = getHydrogenTanks();
  foreach (IMyOxygenTank tank in tanks) {
   hydro_cur += (Decimal) tank.GetOxygenLevel();
   hydro_total += 1M;
  }
  cur_oxygen_level = has_oxygen_tanks ? (oxy_cur / oxy_total) * 100M : 0;
  cur_hydrogen_level = has_hydrogen_tanks ? (hydro_cur / hydro_total) * 100M : 0;
  string oxy_str = !has_oxygen_tanks ? "N/A" : String.Format("{0:0.0}%",
   cur_oxygen_level);
  string hydro_str = !has_hydrogen_tanks ? "N/A" : String.Format("{0:0.0}%",
   cur_hydrogen_level);

  if (has_oxygen_tanks && oxygen_low_watermark > 0 && cur_oxygen_level + getStoredOxygen() < oxygen_low_watermark) {
   alert = true;
   addAntennaAlert(ALERT_LOW_OXYGEN);
  } else {
   removeAntennaAlert(ALERT_LOW_OXYGEN);
  }
  if (has_hydrogen_tanks && hydrogen_low_watermark > 0 && cur_hydrogen_level + getStoredHydrogen() < hydrogen_low_watermark) {
   alert = true;
   addAntennaAlert(ALERT_LOW_HYDROGEN);
  } else {
   removeAntennaAlert(ALERT_LOW_HYDROGEN);
  }
  if (alert) {
   status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1} WARNING!",
    oxy_str, hydro_str);
   addAlert(CYAN_ALERT);
  } else {
   status_report[STATUS_OXYHYDRO_LEVEL] = String.Format("{0} / {1}",
    oxy_str, hydro_str);
   removeAlert(CYAN_ALERT);
  }
 } else {
  removeAntennaAlert(ALERT_LOW_OXYGEN);
  removeAntennaAlert(ALERT_LOW_HYDROGEN);
  status_report[STATUS_OXYHYDRO_LEVEL] = "";
  removeAlert(CYAN_ALERT);
 }
}

int[] state_cycle_counts;
int cur_cycle_count = 0;

bool canContinue() {
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
 Decimal cycle_p = (Decimal) projected_cycle_count / Runtime.MaxInstructionCount;

 // if we never executed the next state, we leave 60% headroom for our next
 // state (for all we know it could be a big state), otherwise leave at 20%
 // because we already know how much it usually takes and it's unlikely to
 // suddenly become much bigger than what we've seen before
 var cycle_thresh = canEstimate ? 0.8M : 0.4M;

 // check if we are exceeding our stated thresholds (projected 80% cycle
 // count for known states, or 40% up to this point for unknown states)
 bool haveEnoughHeadroom = cycle_p <= cycle_thresh;

 // advance current state and store IL count values
 current_state = next_state;
 cur_cycle_count = cur_i;

 return haveEnoughHeadroom;
}

// check if we are disabled or if we should disable other BARABAS instances
bool canRun() {
 bool isDisabled = Me.CustomName.Contains("DISABLED");
 var pbs = new List < IMyTerminalBlock > ();
 GridTerminalSystem.GetBlocksOfType < IMyProgrammableBlock > (pbs, localGridFilter);

 foreach (var block in pbs) {
  if (block == Me) {
   continue;
  }
  if (!block.CustomName.Contains("BARABAS")) {
   continue;
  }
  // if we aren't disabled, disable all rival BARABAS instances
  if (!isDisabled && !block.CustomName.Contains("DISABLED")) {
   block.SetCustomName(block.CustomName + " [DISABLED]");
  } else if (isDisabled) {
   return false;
  }
 }
 return true;
}

public void Save() {
 // save config block ID to storage
 if (config_block != null) {
  Storage = config_block.EntityId.ToString();
 }
}

// constructor
public Program() {
 // kick off state machine
 states = new Action[] {
  s_refreshGrids,
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
 crisis_mode = CRISIS_MODE_NONE;
 state_cycle_counts = new int[states.Length];

 for (int i = 0; i < state_cycle_counts.Length; i++) {
  state_cycle_counts[i] = 0;
 }

 // determine grid size
 var bd = Me.BlockDefinition.ToString();
 large_grid = bd.Contains("Large");

 // find config block
 if (Storage.Length > 0) {
  long id;
  if (long.TryParse(Storage, out id)) {
   config_block = GridTerminalSystem.GetBlockWithId(id) as IMyTextPanel;
  }
 }
}

public void Main() {
 if (!canRun()) {
  return;
 }
 int num_states = 0;

 // zero out IL counters
 cur_cycle_count = 0;

 // clear set of lists we have refreshed during this iteration
 null_list = new HashSet < List < IMyTerminalBlock >> ();
 do {
  try {
   states[current_state]();
  } catch (BarabasException e) {
   // if we caught our own exception, pass it along
   Echo(e.StackTrace);
   throw;
  } catch (Exception e) {
   Echo(e.StackTrace);
   string msg = String.Format("State: {0} Error: {1}", current_state, e.Message);
   throw new BarabasException(msg, this);
  }
  num_states++;
 } while (canContinue() && num_states < states.Length);

 if (trashSensorsActive()) {
  storeTrash(true);
 }

 // check storage load at each iteration
 checkStorageLoad();

 // check for leaks
 if (can_use_oxygen) {
  checkOxygenLeaks();
 }

 if (refineries_clogged || arc_furnaces_clogged || assemblers_clogged) {
  addAlert(MAGENTA_ALERT);
 } else {
  removeAlert(MAGENTA_ALERT);
 }

 // display status updates
 if (has_status_panels) {
  displayStatusReport();
 }
 string il_str = String.Format("IL Count: {0}/{1} ({2:0.0}%)",
  Runtime.CurrentInstructionCount,
  Runtime.MaxInstructionCount,
  (Decimal) Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount * 100M);
 Echo(String.Format("States executed: {0}", num_states));
 Echo(il_str);
}
