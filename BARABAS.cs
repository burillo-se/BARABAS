/*
 * BARABAS v1.4beta3
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
 * - Don't go to town with pistons and rotors - while things will likely work,
 *   it is not recommended and may produce weird results.
 *
 * Optional requirements:
 * - Group of text/LCD panels/beacons/antennas/lights named "BARABAS Notify",
 *   used for notification and status reports.
 * - Text block named "BARABAS Config", used for storing configuration (if not
 *   present, automatic configuration will be used)
 * - If multiple connectors present, there has to be a connector named "BARABAS
 *   Trash", designated for ore disposal (otherwise a random connector will be
 *   used instead)
 * - A sensor named "BARABAS Trash Sensor", to stop throwing out. Set it up
 *   yourself just as you normally would, but don't assign any actions to it -
 *   BARABAS will handle everything.
 *
 * NOTE: if you are using BARABAS from source, you will have to minify the
 *       code before pasting it into the programmable block!
 *
 */

// configuration
const int OP_MODE_AUTO = 0x0;
const int OP_MODE_SHIP = 0x1;
const int OP_MODE_DRILL = 0x2 | OP_MODE_SHIP;
const int OP_MODE_GRINDER = 0x4 | OP_MODE_SHIP;
const int OP_MODE_WELDER = 0x8 | OP_MODE_SHIP;
const int OP_MODE_TUG = 0x10 | OP_MODE_SHIP;
const int OP_MODE_BASE = 0x100;

int op_mode = OP_MODE_AUTO;
Decimal power_high_watermark = 0M;
Decimal power_low_watermark = 0M;
bool throw_out_stone = false;
bool sort_storage = true;
bool hud_notifications = true;
Decimal oxygen_threshold = 15M;
Decimal hydrogen_threshold = 0M;
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

Action [] states = null;

// config options
const string CONFIGSTR_OP_MODE = "mode";
const string CONFIGSTR_POWER_LOW_WATERMARK = "power low watermark";
const string CONFIGSTR_POWER_HIGH_WATERMARK = "power high watermark";
const string CONFIGSTR_PUSH_ORE = "push ore to base";
const string CONFIGSTR_PUSH_INGOTS = "push ingots to base";
const string CONFIGSTR_PUSH_COMPONENTS = "push components to base";
const string CONFIGSTR_PULL_ORE = "pull ore from base";
const string CONFIGSTR_PULL_INGOTS = "pull ingots from base";
const string CONFIGSTR_PULL_COMPONENTS = "pull components from base";
const string CONFIGSTR_KEEP_STONE = "keep stone";
const string CONFIGSTR_SORT_STORAGE = "sort storage";
const string CONFIGSTR_HUD_NOTIFICATIONS = "HUD notifications";
const string CONFIGSTR_OXYGEN_THRESHOLD = "oxygen threshold";
const string CONFIGSTR_HYDROGEN_THRESHOLD = "hydrogen threshold";

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
const string STATUS_STORAGE_LOAD = "Total storage load";
const string STATUS_POWER_STATS = "Power (max/cur/left)";
const string STATUS_ALERT = "Alerts";
const string STATUS_CRISIS_MODE = "Crisis mode";
const string STATUS_OXYHYDRO_LEVEL = "O2/H2";

const Decimal CHUNK_SIZE = 1000M;

// config options, caseless dictionary
readonly Dictionary < string, string > config_options = new Dictionary < string, string > (StringComparer.OrdinalIgnoreCase) {
	{
		CONFIGSTR_OP_MODE, ""
	}, {
		CONFIGSTR_HUD_NOTIFICATIONS, ""
	}, {
		CONFIGSTR_POWER_LOW_WATERMARK, ""
	}, {
		CONFIGSTR_POWER_HIGH_WATERMARK, ""
	}, {
		CONFIGSTR_OXYGEN_THRESHOLD, ""
	}, {
		CONFIGSTR_HYDROGEN_THRESHOLD, ""
	}, {
		CONFIGSTR_PUSH_ORE, ""
	}, {
		CONFIGSTR_PUSH_INGOTS, ""
	}, {
		CONFIGSTR_PUSH_COMPONENTS, ""
	}, {
		CONFIGSTR_PULL_ORE, ""
	}, {
		CONFIGSTR_PULL_INGOTS, ""
	}, {
		CONFIGSTR_PULL_COMPONENTS, ""
	}, {
		CONFIGSTR_KEEP_STONE, ""
	}, {
		CONFIGSTR_SORT_STORAGE, ""
	}
};

// status report fields
readonly Dictionary < string, string > status_report = new Dictionary < string, string > {
	{
		STATUS_STORAGE_LOAD, ""
	}, {
		STATUS_POWER_STATS, ""
	}, {
		STATUS_OXYHYDRO_LEVEL, ""
	}, {
		STATUS_MATERIAL, ""
	}, {
		STATUS_ALERT, ""
	}, {
		STATUS_CRISIS_MODE, ""
	},
};

readonly List < string > ore_types = new List < string > {
	COBALT, GOLD, IRON, MAGNESIUM, NICKEL, PLATINUM,
	SILICON, SILVER, URANIUM, STONE, ICE
};

readonly List < string > arc_furnace_ores = new List < string > {
	COBALT, IRON, NICKEL
};

// ballpark values of "just enough" for each material
readonly Dictionary < string, Decimal > material_thresholds = new Dictionary < string, Decimal > {
	{
		COBALT, 500M
	}, {
		GOLD, 100M
	}, {
		IRON, 5000M
	}, {
		MAGNESIUM, 100M
	}, {
		NICKEL, 1000M
	}, {
		PLATINUM, 10M
	}, {
		SILICON, 1000M
	}, {
		SILVER, 1000M
	}, {
		URANIUM, 10M
	}, {
		STONE, 5000M
	},
};

readonly Dictionary < string, Decimal > ore_to_ingot_ratios = new Dictionary < string, Decimal > {
	{
		COBALT, 0.24M
	}, {
		GOLD, 0.008M
	}, {
		IRON, 0.56M
	}, {
		MAGNESIUM, 0.0056M
	}, {
		NICKEL, 0.32M
	}, {
		PLATINUM, 0.004M
	}, {
		SILICON, 0.56M
	}, {
		SILVER, 0.08M
	}, {
		URANIUM, 0.0056M
	}, {
		STONE, 0.72M
	},
};

// statuses for ore and ingots
static readonly Dictionary < string, Decimal > ore_status = new Dictionary < string, Decimal > {
	{
		COBALT, 0
	}, {
		GOLD, 0
	}, {
		ICE, 0
	}, {
		IRON, 0
	}, {
		MAGNESIUM, 0
	}, {
		NICKEL, 0
	}, {
		PLATINUM, 0
	}, {
		SILICON, 0
	}, {
		SILVER, 0
	}, {
		URANIUM, 0
	}, {
		STONE, 0
	},
};
readonly Dictionary < string, Decimal > ingot_status = new Dictionary < string, Decimal > (ore_status);
readonly Dictionary < string, Decimal > storage_ore_status = new Dictionary < string, Decimal > (ore_status);
readonly Dictionary < string, Decimal > storage_ingot_status = new Dictionary < string, Decimal > (ore_status);

/* local data storage, updated once every few cycles */
List < IMyTerminalBlock > local_blocks = null;
List < IMyTerminalBlock > local_reactors = null;
List < IMyTerminalBlock > local_batteries = null;
List < IMyTerminalBlock > local_refineries = null;
List < IMyTerminalBlock > local_arc_furnaces = null;
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
List < IMyTerminalBlock > local_antennas = null;
List < IMyTerminalBlock > remote_storage = null;
List < IMyTerminalBlock > remote_ship_storage = null;
List < IMyTerminalBlock > remote_reactors = null;
IMyShipConnector trash_connector = null;
IMySensorBlock trash_sensor = null;
IMyProgrammableBlock local_pb = null;
IMyTextPanel config_block = null;
List < IMyCubeGrid > local_grids = null;
List < IMyCubeGrid > remote_base_grids = null;
List < IMyCubeGrid > remote_ship_grids = null;

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

public struct Alert {
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

Dictionary < IMyTerminalBlock, int > blocks_to_alerts = new Dictionary < IMyTerminalBlock, int >();

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

readonly Dictionary < int, string > block_alerts = new Dictionary < int, string > {
	{ALERT_DAMAGED, "Damaged"},
	{ALERT_CLOGGED, "Clogged"},
	{ALERT_MATERIALS_MISSING, "Materials missing"},
	{ALERT_LOW_POWER, "Low power"},
	{ALERT_LOW_STORAGE, "Low storage"},
	{ALERT_VERY_LOW_STORAGE, "Very low storage"},
	{ALERT_MATERIAL_SHORTAGE, "Material shortage"},
	{ALERT_OXYGEN_LEAK, "Oxygen leak"},
	{ALERT_LOW_OXYGEN, "Low oxygen"},
	{ALERT_LOW_HYDROGEN, "Low hydrogen"},
};

/* misc local data */
bool init = false;
bool power_above_threshold = false;
Decimal cur_power_draw;
Decimal max_power_draw;
Decimal max_battery_output;
Decimal max_reactor_output;
Decimal cur_reactor_output;
bool tried_throwing = false;
bool auto_refuel_ship;
bool prioritize_uranium = false;
bool can_use_ingots;
bool can_use_oxygen;
bool can_refine;
bool has_air_vents;
bool has_status_panels;
bool has_reactors;
bool has_welders;
bool has_drills;
bool has_grinders;
bool has_connectors;
bool has_oxygen_tanks;
bool has_hydrogen_tanks;
bool has_single_connector;
bool has_trash_sensor;
bool has_refineries;
bool has_arc_furnaces;
bool connected;
bool connected_to_base;
bool connected_to_ship;

// thrust block definitions
Dictionary < string, Decimal > thrust_power = new Dictionary < string, Decimal > () {
	{"MyObjectBuilder_Thrust/SmallBlockSmallThrust", 33.6M },
	{"MyObjectBuilder_Thrust/SmallBlockLargeThrust", 400M },
	{"MyObjectBuilder_Thrust/LargeBlockSmallThrust", 560M },
	{"MyObjectBuilder_Thrust/LargeBlockLargeThrust", 6720M },
	{"MyObjectBuilder_Thrust/SmallBlockSmallHydrogenThrust", 0M },
	{"MyObjectBuilder_Thrust/SmallBlockLargeHydrogenThrust", 0M },
	{"MyObjectBuilder_Thrust/LargeBlockSmallHydrogenThrust", 0M },
	{"MyObjectBuilder_Thrust/LargeBlockLargeHydrogenThrust", 0M },
	{"MyObjectBuilder_Thrust/SmallBlockSmallAtmosphericThrust", 700M },
	{"MyObjectBuilder_Thrust/SmallBlockLargeAtmosphericThrust", 2400M },
	{"MyObjectBuilder_Thrust/LargeBlockSmallAtmosphericThrust", 2360M },
	{"MyObjectBuilder_Thrust/LargeBlockLargeAtmosphericThrust", 16360M },
};

// power constants - in kWatts
const Decimal URANIUM_INGOT_POWER = 68760M;

public struct ItemHelper {
	public IMyInventory Inventory;
	public IMyInventoryItem Item;
	public int Index;
}

// just have a method to indicate that this exception comes from BARABAS
class BarabasException: Exception {
	public BarabasException(string msg): base("BARABAS: " + msg) {}
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

// this includes ALL local blocks, used for purposes of calculating max power consumption
bool localGridDumbFilter(IMyTerminalBlock block) {
	if (!(block as IMyCubeBlock).IsFunctional) {
		return false;
	}
	return getLocalGrids().Contains(block.CubeGrid);
}
bool localGridFilter(IMyTerminalBlock block) {
	if (block.CustomName.StartsWith("X")) {
		return false;
	}
	return getLocalGrids().Contains(block.CubeGrid);
}
/*
 * Difference between remoteGridFilter and remoteGridDumbFilter is that
 * remoteGridFilter looks at *known* remote grids (i.e. ones we have established
 * as valid remote grids), while remoteGridDumbFilter takes anything that isn't
 * a known local grid (but may include grids that we don't consider remote, i.e.
 * a ship grid connected to another ship grid)
 */
bool remoteGridFilter(IMyTerminalBlock block) {
	if (excludeBlock(block)) {
		return false;
	}
	return getRemoteGrids().Contains(block.CubeGrid);
}
// this filter is not supposed to be used in normal code
bool remoteGridDumbFilter(IMyTerminalBlock block) {
	if (excludeBlock(block)) {
		return false;
	}
	return !getLocalGrids().Contains(block.CubeGrid);
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
// template functions for filtering blocks
public void filterBlocks < T > (List < IMyTerminalBlock > list, string name_filter = null, string definition_filter = null) {
	for (int i = list.Count - 1; i >= 0; i--) {
		var block = list[i];
		if (!(block is T)) {
			list.RemoveAt(i);
		} else if (name_filter != null && block.CustomName != name_filter) {
			list.RemoveAt(i);
		} else if (definition_filter != null && !block.BlockDefinition.ToString().Contains(definition_filter)) {
			list.RemoveAt(i);
		}
	}
}

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

// remove null blocks from list
List<IMyTerminalBlock> removeNulls(List<IMyTerminalBlock> list, int invCount) {
	if (invCount == 0) {
		return list;
	}
	for (int i = list.Count - 1; i >= 0; i--) {
	 var block = list[i];
	 if (block.GetInventoryCount() != invCount) {
		 list.RemoveAt(i);
	 }
	}
	return list;
}

IMySlimBlock slimBlock(IMyTerminalBlock block) {
	return block.CubeGrid.GetCubeBlock(block.Position);
}

// get local blocks
List < IMyTerminalBlock > getBlocks(bool force_update = false) {
	if (local_blocks != null && !force_update) {
		return local_blocks;
	}
	local_blocks = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType
					< IMyTerminalBlock > (local_blocks, localGridFilter);
	bool alert = false;
	// check if we have unfinished blocks
	for (int i = local_blocks.Count - 1; i >= 0; i--) {
		var block = local_blocks[i];
		if (!slimBlock(block).IsFullIntegrity) {
			alert = true;
			addBlockAlert(block, ALERT_DAMAGED);
			if (!block.IsFunctional) {
				local_blocks.RemoveAt(i);
			}
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
		return removeNulls(local_reactors, 1);
	}
	local_reactors = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyReactor > (local_reactors);
	for (int i = 0; i < local_reactors.Count; i++) {
		var inv = local_reactors[i].GetInventory(0);
		if (inv.GetItems().Count > 1) {
			consolidate(inv);
		}
	}
	return local_reactors;
}

List < IMyTerminalBlock > getBatteries(bool force_update = false) {
	if (local_batteries != null && !force_update) {
		return local_batteries;
	}
	local_batteries = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyBatteryBlock > (local_batteries);
	for (int i = local_batteries.Count - 1; i >= 0; i--) {
		if ((local_batteries[i] as IMyBatteryBlock).OnlyRecharge) {
			local_batteries.RemoveAt(i);
		}
	}
	return local_batteries;
}

List < IMyTerminalBlock > getStorage(bool force_update = false) {
	if (local_storage != null && !force_update) {
		return removeNulls(local_storage, 1);
	}
	local_storage = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyCargoContainer > (local_storage);
	for (int i = 0; i < local_storage.Count; i++) {
		var inv = local_storage[i].GetInventory(0);
		consolidate(inv);
	}
	return local_storage;
}

List < IMyTerminalBlock > getRefineries(bool force_update = false) {
	if (local_refineries != null && !force_update) {
		return removeNulls(local_refineries, 2);
	}
	refineries_clogged = false;
	local_refineries = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyRefinery > (local_refineries, null, "LargeRefinery");
	for (int i = 0; i < local_refineries.Count; i++) {
		var refinery = local_refineries[i] as IMyRefinery;
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
	return local_refineries;
}

List < IMyTerminalBlock > getArcFurnaces(bool force_update = false) {
	if (local_arc_furnaces != null && !force_update) {
		return removeNulls(local_arc_furnaces, 2);
	}
	arc_furnaces_clogged = false;
	local_arc_furnaces = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyRefinery > (local_arc_furnaces, null, "Blast Furnace");
	bool alert = false;
	for (int i = 0; i < local_arc_furnaces.Count; i++) {
		var refinery = local_arc_furnaces[i] as IMyRefinery;
		var input_inv = refinery.GetInventory(0);
		var output_inv = refinery.GetInventory(1);
		Decimal input_load = (Decimal) input_inv.CurrentVolume / (Decimal) input_inv.MaxVolume;
		Decimal output_load = (Decimal) output_inv.CurrentVolume / (Decimal) output_inv.MaxVolume;
		if (!refinery.IsQueueEmpty && !refinery.IsProducing) {
			addBlockAlert(refinery, ALERT_CLOGGED);
			arc_furnaces_clogged = true;
		} else {
			removeBlockAlert(refinery, ALERT_CLOGGED);
		}
		displayBlockAlerts(refinery);
	}
	return local_arc_furnaces;
}

List < IMyTerminalBlock > getAllRefineries() {
	var list = new List < IMyTerminalBlock > ();
	list.AddRange(getRefineries());
	list.AddRange(getArcFurnaces());
	return list;
}

List < IMyTerminalBlock > getAssemblers(bool force_update = false) {
	if (local_assemblers != null && !force_update) {
		return removeNulls(local_assemblers, 2);
	}
	assemblers_clogged = false;
	local_assemblers = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyAssembler > (local_assemblers);
	bool alert = false;
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
		return removeNulls(local_connectors, 1);
	}
	local_connectors = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyShipConnector > (local_connectors);
	for (int i = 0; i < local_connectors.Count; i++) {
		consolidate(local_connectors[i].GetInventory(0));
	}
	return local_connectors;
}

// get notification lights
List < IMyTerminalBlock > getLights(bool force_update = false) {
	if (local_lights != null && !force_update) {
		return local_lights;
	}
	// find our group
	local_lights = new List < IMyTerminalBlock > ();
	var groups = new List<IMyBlockGroup>();
	GridTerminalSystem.GetBlockGroups(groups);
	for (int i = 0; i < groups.Count; i++) {
		var group = groups[i];
		// skip groups we don't want
		if (group.Name != "BARABAS Notify") {
			continue;
		}
		var blocks = group.Blocks;
		// we may find multiple Notify groups, as we may have a BARABAS-driven
		// ships connected, so let's filter lights
		filterLocalGrid < IMyLightingBlock > (blocks);

		// now we know it's our lights group, so store it
		local_lights = blocks;
		break;
	}
	return local_lights;
}

// get status report text panels
List < IMyTerminalBlock > getTextPanels(bool force_update = false) {
	if (local_text_panels != null && !force_update) {
		return local_text_panels;
	}
	// find our group
	local_text_panels = new List < IMyTerminalBlock > ();
	var groups = new List<IMyBlockGroup>();
	GridTerminalSystem.GetBlockGroups(groups);
	for (int i = 0; i < groups.Count; i++) {
		var group = groups[i];
		// skip groups we don't want
		if (group.Name != "BARABAS Notify") {
			continue;
		}
		var blocks = group.Blocks;
		// we may find multiple Status groups, as we may have a BARABAS-driven
		// ships connected, so let's filter text panels
		filterLocalGrid < IMyTextPanel > (blocks);

		// if the user accidentally included a config block into this group,
		// notify him immediately
		if (blocks.Contains(getConfigBlock() as IMyTerminalBlock)) {
			throw new BarabasException("Configuration text panel should not " +
				"be part of BARABAS Notify group");
		}

		// now we know it's our text panels group, so store it
		local_text_panels = blocks;
		break;
	}
	return local_text_panels;
}

// get status report text panels
List < IMyTerminalBlock > getAntennas(bool force_update = false) {
	if (local_text_panels != null && !force_update) {
		return local_antennas;
	}
	// find our group
	local_antennas = new List < IMyTerminalBlock > ();
	var groups = new List<IMyBlockGroup>();
	GridTerminalSystem.GetBlockGroups(groups);
	for (int i = 0; i < groups.Count; i++) {
		var group = groups[i];
		// skip groups we don't want
		if (group.Name != "BARABAS Notify") {
			continue;
		}
		var tmp_antennas = group.Blocks;
		var tmp_beacons = group.Blocks;
		var tmp_laser = group.Blocks;

		// we may find multiple Status groups, as we may have a BARABAS-driven
		// ships connected, so let's filter text panels
		filterLocalGrid < IMyBeacon > (tmp_beacons);
		filterLocalGrid < IMyRadioAntenna > (tmp_antennas);
		filterLocalGrid < IMyLaserAntenna > (tmp_laser);

		// populate the list
		for (int j = 0; j < tmp_beacons.Count; j++) {
			local_antennas.Add(tmp_beacons[j]);
		}
		for (int j = 0; j < tmp_antennas.Count; j++) {
			local_antennas.Add(tmp_antennas[j]);
		}
		for (int j = 0; j < tmp_laser.Count; j++) {
			local_antennas.Add(tmp_laser[j]);
		}
		break;
	}
	return local_antennas;
}

List < IMyTerminalBlock > getDrills(bool force_update = false) {
	if (local_drills != null && !force_update) {
		return removeNulls(local_drills, 1);
	}
	local_drills = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyShipDrill > (local_drills);
	for (int i = 0; i < local_drills.Count; i++) {
		consolidate(local_drills[i].GetInventory(0));
	}
	return local_drills;
}

List < IMyTerminalBlock > getGrinders(bool force_update = false) {
	if (local_grinders != null && !force_update) {
		return removeNulls(local_grinders, 1);
	}
	local_grinders = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyShipGrinder > (local_grinders);
	for (int i = 0; i < local_grinders.Count; i++) {
		consolidate(local_grinders[i].GetInventory(0));
	}
	return local_grinders;
}

List < IMyTerminalBlock > getWelders(bool force_update = false) {
	if (local_welders != null && !force_update) {
		return removeNulls(local_welders, 1);
	}
	local_welders = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyShipWelder > (local_welders);
	for (int i = 0; i < local_welders.Count; i++) {
		consolidate(local_welders[i].GetInventory(0));
	}
	return local_welders;
}

List<IMyTerminalBlock> getTanks(string type) {
	var list = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyOxygenTank > (list, null, type);
	return list;
}

List < IMyTerminalBlock > getAirVents(bool force_update = false) {
	if (local_air_vents != null && !force_update) {
		return local_air_vents;
	}
	local_air_vents = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyAirVent > (local_air_vents);
	return local_air_vents;
}

List < IMyTerminalBlock > getOxygenTanks(bool force_update = false) {
	if (local_oxygen_tanks != null && !force_update) {
		return local_oxygen_tanks;
	}
	local_oxygen_tanks = getTanks("Oxygen");
	// hydrogen tanks are counted as oxygen tanks here, so filter them out
	for (int i = local_oxygen_tanks.Count - 1; i >= 0; i--) {
		var block = local_oxygen_tanks[i];
		if (block.BlockDefinition.ToString().Contains("Hydrogen")) {
			local_oxygen_tanks.RemoveAt(i);
		}
	}
	return local_oxygen_tanks;
}

List < IMyTerminalBlock > getHydrogenTanks(bool force_update = false) {
	if (local_hydrogen_tanks != null && !force_update) {
		return local_hydrogen_tanks;
	}
	local_hydrogen_tanks = getTanks("Hydrogen");
	return local_hydrogen_tanks;
}

List < IMyTerminalBlock > getOxygenGenerators(bool force_update = false) {
	if (local_oxygen_generators != null && !force_update) {
		return removeNulls(local_oxygen_generators, 1);
	}
	local_oxygen_generators = new List < IMyTerminalBlock > (getBlocks());
	filterBlocks < IMyOxygenGenerator > (local_oxygen_generators);
	return local_oxygen_generators;
}

string getGridId(string val) {
	var regex = new System.Text.RegularExpressions.Regex("\\{([\\dA-F]+)\\}");
	var match = regex.Match(val);
	if (!match.Success || !match.Groups[1].Success) {
		throw new BarabasException("Unknown grid id format");
	}
	return match.Groups[1].Value;
}

void loadLocalGrids(List < IMyCubeGrid > grids) {
	grids.Add(Me.CubeGrid);
	if (Storage.Length > 0) {
		var tentative_grids = new List < IMyCubeGrid > ();
		var blocks = new List < IMyTerminalBlock > ();
		string[] ids = Storage.Split(':');
		GridTerminalSystem.GetBlocksOfType < IMyTerminalBlock > (blocks);
		for (int j = 0; j < blocks.Count; j++) {
			var grid = blocks[j].CubeGrid;
			if (!tentative_grids.Contains(grid)) {
				tentative_grids.Add(grid);
				// check if we know this grid
				var str = getGridId(grid.ToString());
				if (Array.IndexOf(ids, str) != -1) {
					grids.Add(grid);
				}
			}
		}
	}
}

void saveLocalGrids(List < IMyCubeGrid > grids) {
	StringBuilder sb = new StringBuilder();
	for (int i = 0; i < grids.Count; i++) {
		var grid = grids[i];
		sb.Append(getGridId(grid.ToString()));
		sb.Append(":");
	}
	Storage = sb.ToString();
}

// getting local grids is not trivial, we're basically doing some heuristics
List < IMyCubeGrid > getLocalGrids(bool force_update = false) {
	if (local_grids != null && !force_update) {
		return local_grids;
	}
	var tentative_grids = new List < IMyCubeGrid > ();
	tentative_grids.Add(Me.CubeGrid);

	// get all connectors
	List < IMyTerminalBlock > list = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyShipConnector > (list);
	// assume any grid we will find is local, unless we encounter a locked
	// connector, in which case we either don't update the list at all, or, if
	// this is our first time, go with guaranteed-local grid only
	for (int i = 0; i < list.Count; i++) {
		var connector = list[i] as IMyShipConnector;
		if (connector.IsLocked) {
			return local_grids;
		}
		if (!tentative_grids.Contains(connector.CubeGrid)) {
			tentative_grids.Add(connector.CubeGrid);
		}
	}
	var blocks = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyLightingBlock > (blocks);
	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
		if (!tentative_grids.Contains(block.CubeGrid)) {
			tentative_grids.Add(block.CubeGrid);
		}
	}
	local_grids = tentative_grids;
	return local_grids;
}

// a process of getting remote grids comprises getting all non-local storage
// containers and determining whether they are ship grids or base grids by checking
// if there are thrusters on the same grid
void findRemoteGrids() {
	var base_grids = new List < IMyCubeGrid > ();
	var ship_grids = new List < IMyCubeGrid > ();
	var skip_grids = new List < IMyCubeGrid > ();

	// first, get all locked remote connectors and all remote storage
	List < IMyTerminalBlock > list = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyCargoContainer > (list, remoteGridDumbFilter);

	// reset all grids
	if (list.Count == 0) {
		remote_base_grids = new List < IMyCubeGrid > ();
		remote_ship_grids = new List < IMyCubeGrid > ();
		connected_to_base = false;
		connected_to_ship = false;
		connected = false;
		return;
	}

	// find all remote thrusters
	List < IMyTerminalBlock > thrusters = new List < IMyTerminalBlock > ();
	List < IMyTerminalBlock > pb = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyThrust > (thrusters, remoteGridDumbFilter);
	GridTerminalSystem.GetBlocksOfType < IMyProgrammableBlock > (pb, remoteGridDumbFilter);

	// find any thruster grids that also have a connector or storage on the same grid
	for (int i = 0; i < thrusters.Count; i++) {
		var thruster_grid = thrusters[i].CubeGrid;
		bool found = false;
		// if we already seen this grid, skip
		if (skip_grids.Contains(thruster_grid) || ship_grids.Contains(thruster_grid)) {
			continue;
		}
		for (int j = 0; j < list.Count; j++) {
			var grid = list[j].CubeGrid;
			if (thruster_grid == grid) {
				// assume it's a ship
				if (!ship_grids.Contains(thruster_grid)) {
					ship_grids.Add(thruster_grid);
				}
				list.RemoveAt(j);
				j--;
				found = true;
				// go on and see if any other block in the list is on the same grid
			}
		}
		// we don't know what the hell it is
		if (!found) {
			skip_grids.Add(thruster_grid);
		}
	}
	// anything that wasn't filtered out previously, is a base grid. at least
	// we hope it is.
	for (int i = 0; i < list.Count; i++) {
		var grid = list[i].CubeGrid;
		if (!base_grids.Contains(grid)) {
			base_grids.Add(list[i].CubeGrid);
		}
	}

	// now, override anything we've found if there are BARABAS instances on those grids
	for (int i = base_grids.Count - 1; i >= 0; i--) {
		var grid = base_grids[i];
		// now go through programmable blocks
		for (int j = 0; j < pb.Count; j++) {
			var pb_grid = pb[j].CubeGrid;
			if (pb_grid == grid) {
				var name = pb[j].CustomName;
				if (name == "BARABAS Ship CPU") {
					ship_grids.Add(grid);
					base_grids.RemoveAt(i);
					break;
				}
			}
		}
	}
	for (int i = ship_grids.Count - 1; i >= 0; i--) {
		var grid = ship_grids[i];
		// now go through programmable blocks
		for (int j = 0; j < pb.Count; j++) {
			var pb_grid = pb[j].CubeGrid;
			if (pb_grid == grid) {
				var name = pb[j].CustomName;
				if (name == "BARABAS Base CPU") {
					base_grids.Add(grid);
					ship_grids.RemoveAt(i);
					break;
				}
			}
		}
	}

	// having multiple bases is not supported
	if (base_grids.Count > 1) {
		throw new BarabasException("Connecting to multiple bases is not supported!");
	}
	remote_base_grids = base_grids;
	remote_ship_grids = ship_grids;
	connected_to_base = base_grids.Count > 0;
	connected_to_ship = ship_grids.Count > 0;
	connected = connected_to_base || connected_to_ship;
}

List < IMyCubeGrid > getBaseGrids() {
	return remote_base_grids;
}

List < IMyCubeGrid > getShipGrids() {
	return remote_ship_grids;
}

List < IMyCubeGrid > getRemoteGrids() {
	if (op_mode == OP_MODE_BASE) {
		return remote_ship_grids;
	} else {
		return remote_base_grids;
	}
}

List < IMyTerminalBlock > getRemoteStorage(bool force_update = false) {
	if (remote_storage != null && !force_update) {
		return removeNulls(remote_storage, 1);
	}
	remote_storage = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyCargoContainer > (remote_storage, remoteGridFilter);
	for (int i = 0; i < remote_storage.Count; i++) {
		consolidate(remote_storage[i].GetInventory(0));
	}
	return remote_storage;
}

List < IMyTerminalBlock > getRemoteShipStorage(bool force_update = false) {
	if (remote_ship_storage != null && !force_update) {
		return removeNulls(remote_ship_storage, 1);
	}
	remote_ship_storage = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyCargoContainer > (remote_ship_storage, shipFilter);
	for (int i = 0; i < remote_ship_storage.Count; i++) {
		consolidate(remote_ship_storage[i].GetInventory(0));
	}
	return remote_ship_storage;
}

// get local trash disposal connector
IMyShipConnector getTrashConnector(bool force_update = false) {
	if (!force_update && trash_connector != null) {
		return trash_connector;
	}
	var blocks = getConnectors();
	filterBlocks < IMyShipConnector > (blocks, "BARABAS Trash");
	trash_connector = null;
	// if we can't find a dedicated trash connector, use first available
	if (blocks.Count < 1) {
		var connectors = getConnectors();
		if (connectors.Count > 0) {
			trash_connector = connectors[0] as IMyShipConnector;
		}
	} else if (blocks.Count > 1) {
		throw new BarabasException("Multiple trash blocks found!");
	} else {
		trash_connector = blocks[0] as IMyShipConnector;
	}
	return trash_connector;
}

// get local trash disposal connector sensor
IMySensorBlock getTrashSensor(bool force_update = false) {
	if (!force_update && trash_sensor != null) {
		return trash_sensor;
	}
	var blocks = new List < IMyTerminalBlock> (getBlocks());
	filterBlocks < IMySensorBlock > (blocks, "BARABAS Trash Sensor");
	if (blocks.Count < 1) {
		trash_sensor = null;
	} else if (blocks.Count > 1) {
		throw new BarabasException("Multiple trash sensors found!");
	} else {
		trash_sensor = blocks[0] as IMySensorBlock;
	}
	return trash_sensor;
}

IMyTextPanel getConfigBlock(bool force_update = false) {
	if (!force_update && config_block != null) {
		return config_block;
	}
	var blocks = new List < IMyTerminalBlock> (getBlocks());
	filterBlocks < IMyTextPanel > (blocks, "BARABAS Config");
	if (blocks.Count < 1) {
		return null;
	} else if (blocks.Count > 1) {
		throw new BarabasException("Multiple config blocks found");
	} else {
		config_block = blocks[0] as IMyTextPanel;
	}
	return config_block;
}

/**
 * Inventory access functions
 */
List < ItemHelper > getAllInventories() {
	List < ItemHelper > list = new List < ItemHelper > ();
	var blocks = getBlocks();
	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
		// skip blocks that don't have inventory
		if ((block as IMyInventoryOwner) != null && !block.HasInventory()) {
			continue;
		}
		int invCount = block.GetInventoryCount();
		for (int ci = 0; ci < invCount; ci++) {
			var inv = block.GetInventory(ci);
			ItemHelper ih = new ItemHelper();
			ih.Inventory = inv;
			list.Add(ih);
		}
	}
	return list;
}

// get everything in a particular inventory
void getAllItems(IMyInventory inv, List < ItemHelper > list) {
	var items = inv.GetItems();
	for (int i = items.Count - 1; i >= 0; i--) {
		var item = items[i];
		ItemHelper ih = new ItemHelper();
		ih.Inventory = inv;
		ih.Item = item;
		ih.Index = i;
		list.Add(ih);
	}
}

// get all ingots of a certain type from a particular inventory
void getAllIngots(IMyInventory inv, string name, List < ItemHelper > list) {
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
		ih.Inventory = inv;
		ih.Item = item;
		ih.Index = i;
		list.Add(ih);
	}
}

// get all local ingots of a certain type
List < ItemHelper > getAllIngots(string name) {
	List < ItemHelper > list = new List < ItemHelper > ();
	var inventories = getAllInventories();
	for (int i = 0; i < inventories.Count; i++) {
		var inv = inventories[i].Inventory;
		getAllIngots(inv, name, list);
	}
	return list;
}

// get all local ore of a certain type
List < ItemHelper > getAllOre(string name) {
	List < ItemHelper > list = new List < ItemHelper > ();
	var inventories = getAllInventories();
	for (int a = 0; a < inventories.Count; a++) {
		var inv = inventories[a].Inventory;
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
			ih.Inventory = inv;
			ih.Item = item;
			ih.Index = i;
			list.Add(ih);
		}
	}
	return list;
}

// get all ingots residing in storage
List < ItemHelper > getAllStorageIngots(string name = null) {
	List < ItemHelper > list = new List < ItemHelper > ();
	var blocks = getStorage();
	for (int i = 0; i < blocks.Count; i++) {
		var inv = blocks[i].GetInventory(0);
		getAllIngots(inv, name, list);
	}
	return list;
}

bool isOre(IMyInventoryItem item) {
	if (item.Content.SubtypeName == "Scrap") {
		return true;
	}
	return item.Content is VRage.Game.MyObjectBuilder_Ore;
}

bool isIngot(IMyInventoryItem item) {
	if (item.Content.SubtypeName == "Scrap") {
		return false;
	}
	return item.Content is VRage.Game.MyObjectBuilder_Ingot;
}

bool isComponent(IMyInventoryItem item) {
	return item.Content is VRage.Game.MyObjectBuilder_Component;
}

// get total amount of all ingots (of a particular type) stored in a particular inventory
Decimal getTotalIngots(IMyInventory inv, string name) {
	var entries = new List < ItemHelper > ();
	getAllIngots(inv, name, entries);
	Decimal ingots = 0;
	for (int i = 0; i < entries.Count; i++) {
		var entry = entries[i];
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

bool Transfer(IMyInventory src_inv, IMyInventory dst_inv, int srcIndex,
Nullable < int > dstIndex, Nullable < bool > stack, Nullable < VRage.MyFixedPoint > amount) {
	if (dst_inv == src_inv) {
		return true;
	}

	var curVolume = dst_inv.CurrentVolume;

	if (!src_inv.TransferItemTo(dst_inv, srcIndex, dstIndex, stack, amount)) {
		var sb = new StringBuilder();
		sb.Append("Error transfering from ");
		sb.Append((src_inv.Owner as IMyTerminalBlock).CustomName);
		sb.Append(" to ");
		sb.Append((dst_inv.Owner as IMyTerminalBlock).CustomName);
		Echo(sb.ToString());
		Echo("Check conveyors for missing/damage and\nblock ownership");
		return false;
	}

	// now, check if we actually transferred anything
	return dst_inv.CurrentVolume != curVolume;
}

void pushBack(IMyInventory src, int srcIndex, Nullable < VRage.MyFixedPoint > amount) {
	src.TransferItemTo(src, srcIndex, src.GetItems().Count, true, amount);
}

void pushFront(IMyInventory src, int srcIndex, Nullable < VRage.MyFixedPoint > amount) {
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
	for (int i = 0; i < storage.Count; i++) {
		var container = storage[i] as IMyCargoContainer;
		cur_volume += (Decimal) container.GetInventory(0).CurrentVolume;
		max_volume += (Decimal) container.GetInventory(0).MaxVolume;
	}
	ratio = Math.Round(cur_volume / max_volume, 2);

	if (op_mode == OP_MODE_DRILL || op_mode == OP_MODE_GRINDER) {
		ratio = Math.Round(ratio * 0.75M, 4);
	}
	if (op_mode != OP_MODE_DRILL && op_mode != OP_MODE_GRINDER) {
		return ratio;
	}
	// if we're a drill ship or a grinder, also look for block with the biggest load
	if (op_mode == OP_MODE_DRILL) {
		storage = getDrills();
	} else if (op_mode == OP_MODE_GRINDER) {
		storage = getGrinders();
	} else {
		throw new BarabasException("Unknown mode");
	}
	Decimal maxLoad = 0M;
	for (int i = 0; i < storage.Count; i++) {
		var inv = storage[i].GetInventory(0);
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
	for (int i = 0; i < items.Count; i++) {
		var ore = items[i].Content.SubtypeName;
		Decimal amount;
		ores.TryGetValue(ore, out amount);
		ores[ore] = amount + (Decimal) items[i].Amount;
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
	for (int i = 0; i < items.Count; i++) {
		var item = items[i];
		if (!isOre(item)) {
			return false;
		}
	}
	return true;
}

bool hasOnlyIngots(IMyInventory inv) {
	var items = inv.GetItems();
	for (int i = 0; i < items.Count; i++) {
		var item = items[i];
		if (!isIngot(item)) {
			return false;
		}
	}
	return true;
}

bool hasOnlyComponents(IMyInventory inv) {
	var items = inv.GetItems();
	for (int i = 0; i < items.Count; i++) {
		var item = items[i];
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
		status_report[STATUS_STORAGE_LOAD] = "";
		removeAlert(YELLOW_ALERT);
		removeAlert(RED_ALERT);
		return;
	}
	Decimal storageLoad = getTotalStorageLoad();
	if (storageLoad >= 0.98M) {
		addAlert(RED_ALERT);
		removeAlert(YELLOW_ALERT);
		// if we're a base, enter crisis mode
		if (op_mode == OP_MODE_BASE && has_refineries && refineriesClogged()) {
			if (tried_throwing) {
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
	}
	if (storageLoad >= 0.75M && storageLoad < 0.98M) {
		addAlert(YELLOW_ALERT);
	} else if (storageLoad < 0.75M) {
		removeAlert(YELLOW_ALERT);
		if (storageLoad < 0.98M && op_mode == OP_MODE_BASE) {
			if (trashHasUsefulItems()) {
				storeTrash();
			}
			tried_throwing = false;
		}
	}
	status_report[STATUS_STORAGE_LOAD] = String.Format("{0}%", Math.Round(storageLoad * 100M, 0));
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

			pushToStorage(inv, i, null);

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
bool pushToStorage(IMyInventory src, int srcIndex, Nullable < VRage.MyFixedPoint > amount) {
	var containers = getStorage();
	/*
	 * Stage 0: special case for small container numbers, or if sorting is
	 * disabled. Basically, don't sort.
	 */

	if (containers.Count < 3 || !sort_storage) {
		for (int i = 0; i < containers.Count; i++) {
			var container_inv = containers[i].GetInventory(0);
			// try pushing to this container
			if (Transfer(src, container_inv, srcIndex, null, true, amount)) {
				return true;
			}
		}
		return false;
	}

	/*
	 * Stage 1: try to put stuff into designated containers
	 */
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
		if (Transfer(src, container_inv, srcIndex, null, true, amount)) {
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
		var dst = containers[overflowIdx].GetInventory(0);
		if (Transfer(src, dst, srcIndex, null, true, amount)) {
			return true;
		}
	}
	if (emptyIdx != -1) {
		var dst = containers[emptyIdx].GetInventory(0);
		if (Transfer(src, dst, srcIndex, null, true, amount)) {
			return true;
		}
	}
	if (leastFullIdx != -1) {
		var dst = containers[leastFullIdx].GetInventory(0);
		if (Transfer(src, dst, srcIndex, null, true, amount)) {
			return true;
		}
	}
	return false;
}

// try pushing something to one of the remote storage containers
bool pushToRemoteStorage(IMyInventory src, int srcIndex, Nullable < VRage.MyFixedPoint > amount) {
	var containers = getRemoteStorage();
	for (int i = 0; i < containers.Count; i++) {
		var container_inv = containers[i].GetInventory(0);
		Decimal freeVolume = ((Decimal) container_inv.MaxVolume - (Decimal) container_inv.CurrentVolume) * 1000M;
		if (freeVolume < 1M) {
			continue;
		}
		int count = container_inv.GetItems().Count;
		// try pushing to this container
		if (Transfer(src, container_inv, srcIndex, null, true, amount)) {
			return true;
		}
	}
	return false;
}

// try pushing something to one of the remote storage containers
bool pushToRemoteShipStorage(IMyInventory src, int srcIndex, Nullable < VRage.MyFixedPoint > amount) {
	var containers = getRemoteShipStorage();
	for (int i = 0; i < containers.Count; i++) {
		var container_inv = containers[i].GetInventory(0);
		// try pushing to this container
		if (Transfer(src, container_inv, srcIndex, null, true, amount)) {
			return true;
		}
	}
	return false;
}

void pushAllToRemoteStorage() {
	var storage = getStorage();
	for (int i = 0; i < storage.Count; i++) {
		var container = storage[i];
		var inv = container.GetInventory(0);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			var item = items[j];
			if (isOre(item) && push_ore_to_base) {
				pushToRemoteStorage(inv, j, null);
			}
			if (isIngot(item)) {
				var type = item.Content.SubtypeName;
				if (type != URANIUM && push_ingots_to_base) {
					pushToRemoteStorage(inv, j, null);
				}
			}
			if (isComponent(item) && push_components_to_base) {
				pushToRemoteStorage(inv, j, null);
			}
		}
	}
}

void pullFromRemoteShipStorage() {
	var storage = getRemoteShipStorage();
	for (int i = 0; i < storage.Count; i++) {
		var container = storage[i];
		var inv = container.GetInventory(0);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			var item = items[j];
			if (isOre(item) && push_ore_to_base) {
				pushToStorage(inv, j, null);
			}
			if (isIngot(item)) {
				var type = item.Content.SubtypeName;
				// don't take all uranium from base
				if (type != URANIUM && push_ingots_to_base) {
					pushToStorage(inv, j, null);
				}
			}
			if (isComponent(item) && push_components_to_base) {
				pushToStorage(inv, j, null);
			}
		}
	}
}

void pushToRemoteShipStorage() {
	var storage = getStorage();
	for (int i = 0; i < storage.Count; i++) {
		var container = storage[i];
		var inv = container.GetInventory(0);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			var item = items[j];
			if (isOre(item) && pull_ore_from_base) {
				pushToRemoteShipStorage(inv, j, null);
			}
			if (isIngot(item)) {
				var type = item.Content.SubtypeName;
				if (type != URANIUM && pull_ingots_from_base) {
					pushToRemoteShipStorage(inv, j, null);
				}
			}
			if (isComponent(item) && pull_components_from_base) {
				pushToRemoteShipStorage(inv, j, null);
			}
		}
	}
}

void pullFromRemoteStorage() {
	if (op_mode == OP_MODE_BASE) {
		return;
	}
	var storage = getRemoteStorage();
	for (int i = 0; i < storage.Count; i++) {
		var container = storage[i];
		var inv = container.GetInventory(0);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			var item = items[j];
			if (isOre(item) && pull_ore_from_base) {
				pushToStorage(inv, j, null);
			}
			if (isIngot(item)) {
				var type = item.Content.SubtypeName;
				// don't take all uranium from base
				if (type == URANIUM && auto_refuel_ship && !aboveHighWatermark()) {
					pushToStorage(inv, j, (VRage.MyFixedPoint) Math.Min(0.5M, (Decimal) item.Amount));
				} else if (type != URANIUM && pull_ingots_from_base) {
					pushToStorage(inv, j, null);
				}
			}
			if (isComponent(item) && pull_components_from_base) {
				pushToStorage(inv, j, null);
			}
		}
	}
}

void emptyBlocks(List < IMyTerminalBlock > blocks) {
	for (int i = 0; i < blocks.Count; i++) {
		var inv = blocks[i].GetInventory(0);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			pushToStorage(inv, j, null);
		}
	}
}

void fillWelders() {
	var welders = getWelders();
	int s_index = 0;

	for (int i = 0; i < welders.Count; i++) {
		Decimal cur_vol = (Decimal) welders[i].GetInventory(0).CurrentVolume * 1000M;
		Decimal max_vol = (Decimal) welders[i].GetInventory(0).MaxVolume * 1000M;
		Decimal target_volume = max_vol - 400M - cur_vol;
		if (target_volume <= 0) {
			continue;
		}
		var dst_inv = welders[i].GetInventory(0);
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
				if (!Transfer(src_inv, dst_inv, j, null, true, (VRage.MyFixedPoint) 1)) {
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

void pushOreToStorage() {
	var refineries = getAllRefineries();
	for (int i = 0; i < refineries.Count; i++) {
		var inv = refineries[i].GetInventory(0);
		for (int j = inv.GetItems().Count - 1; j >= 0; j--) {
			pushToStorage(inv, j, null);
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
	for (int i = 0; i < reactors.Count; i++) {
		max_reactor_output += (Decimal) (reactors[i] as IMyReactor).MaxOutput * 1000M;
	}

	return max_reactor_output;
}

Decimal getCurReactorPowerOutput(bool force_update = false) {
 if (!force_update) {
	 return cur_reactor_output;
 }

 cur_reactor_output = 0;
 var reactors = getReactors();
 for (int i = 0; i < reactors.Count; i++) {
	 if (reactors[i].IsWorking)
	 	cur_reactor_output += (Decimal) (reactors[i] as IMyReactor).MaxOutput * 1000M;
 }

 return cur_reactor_output;
}

Decimal getMaxBatteryPowerOutput(bool force_update = false) {
 if (!force_update) {
	 return max_battery_output;
 }

 max_battery_output = 0;
 var batteries = getBatteries();
 for (int i = 0; i < batteries.Count; i++) {
	 if ((batteries[i] as IMyBatteryBlock).HasCapacityRemaining)
		 max_battery_output += getMaxOutput(batteries[i]);
 }

 return max_battery_output;
}

Decimal getBatteryStoredPower() {
	var batteries = getBatteries();
	Decimal stored_power = 0;
	for (int i = 0; i < batteries.Count; i++) {
		var battery = batteries[i] as IMyBatteryBlock;
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

Decimal getCurPowerDraw(bool force_update = false) {
	if (!force_update) {
		return cur_power_draw;
	}

	Decimal power_draw = 0;

	// go through all the blocks
	List < IMyTerminalBlock > blocks = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyTerminalBlock > (blocks, localGridDumbFilter);

	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
		if (!(block is IMyBatteryBlock) && !(block is IMyReactor))
			continue;
		power_draw += getBlockPowerOutput(block);
	}

	cur_power_draw = power_draw;

	return cur_power_draw;
}

Decimal getMaxPowerDraw(bool force_update = false) {
	if (!force_update) {
		return max_power_draw;
	}

	Decimal power_draw = 0;

	// go through all the blocks
	List < IMyTerminalBlock > blocks = new List < IMyTerminalBlock > ();
	GridTerminalSystem.GetBlocksOfType < IMyTerminalBlock > (blocks, localGridDumbFilter);

	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
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
					throw new BarabasException("Unknown thrust type");
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

Decimal getBlockPowerOutput(IMyTerminalBlock block) {
	var cur_regex = new System.Text.RegularExpressions.Regex("Current Output: ([\\d\\.]+) (\\w?)W");
	var cur_match = cur_regex.Match(block.DetailedInfo);
	if (!cur_match.Success) {
		return 0;
	}

	Decimal cur = 0;
	if (cur_match.Groups[1].Success && cur_match.Groups[2].Success) {
		bool result = Decimal.TryParse(cur_match.Groups[1].Value, out cur);
		if (!result) {
			throw new BarabasException("Invalid detailed info format!");
		}
		cur *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(cur_match.Groups[2].Value) - 1);
	}
	return cur;
}

Decimal getBlockPowerUse(IMyTerminalBlock block) {
	// Hydrogen thrusters don't use power but still report it
	string typename = (block as IMyCubeBlock).BlockDefinition.ToString();
	if (typename.Contains("HydrogenThrust")) {
		return 0;
	}
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
			throw new BarabasException("Invalid detailed info format!");
		}
		max *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(power_match.Groups[2].Value) - 1);
	}
	if (cur_match.Groups[1].Success && cur_match.Groups[2].Success) {
		bool result = Decimal.TryParse(cur_match.Groups[1].Value, out cur);
		if (!result) {
			throw new BarabasException("Invalid detailed info format!");
		}
		cur *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(cur_match.Groups[2].Value) - 1);
	}
	return Math.Max(cur, max);
}

Decimal getMaxOutput(IMyTerminalBlock reactor) {
	System.Text.RegularExpressions.Regex power_regex = new System.Text.RegularExpressions.Regex("Max Output: ([\\d\\.]+) (\\w?)W");
	System.Text.RegularExpressions.Match match = power_regex.Match(reactor.DetailedInfo);
	if (!match.Success) {
		throw new BarabasException("Unknown reactor info format");
	}

	Decimal power;
	if (!Decimal.TryParse(match.Groups[1].Value, out power)) {
		throw new BarabasException("Unknown reactor info format");
	}
	if (match.Groups[1].Success) {
		power *= (Decimal) Math.Pow(1000.0, " kMGTPEZY".IndexOf(match.Groups[2].Value) - 1);
	}
	return power;
}

Decimal getHighWatermark(Decimal power_use) {
	return power_use * power_high_watermark;
}

Decimal getLowWatermark(Decimal power_use) {
	return power_use * power_low_watermark;
}

bool aboveHighWatermark() {
	var stored_power = getBatteryStoredPower() + getReactorStoredPower();

	// check if we have enough uranium ingots to fill all local reactors and
	// have a few spare ones
	Decimal power_draw;
	if ((op_mode & OP_MODE_SHIP) != 0 && connected_to_base) {
		power_draw = getMaxPowerDraw();
	} else {
		power_draw = getCurPowerDraw();
	}
	Decimal power_needed = getHighWatermark(power_draw);
	Decimal totalPowerNeeded = power_needed * 1.3M;

	if (stored_power > totalPowerNeeded) {
		power_above_threshold = true;
		return true;
	}
	// if we always go by fixed limit, we will constantly have to refine uranium
	// therefore, rather than constantly refining uranium, let's watch a certain
	// threshold and for other ore to be refined while we still have lots of
	// spare uranium
	if (stored_power > power_needed && power_above_threshold) {
		return true;
	}
	// we flip the switch, so next time we decide it's time to leave uranium alone
	// will be when we have uranium above threshold
	power_above_threshold = false;

	return false;
}

bool aboveLowWatermark() {
	Decimal power_draw;
	if ((op_mode & OP_MODE_SHIP) != 0 && connected_to_base) {
		power_draw = getMaxPowerDraw();
	} else {
		power_draw = getCurPowerDraw();
	}
	return getBatteryStoredPower() + getReactorStoredPower() > getLowWatermark(power_draw);
}

bool refillReactors(bool force = false) {
	bool refilled = true;
	ItemHelper ? ingot = null;
	Decimal orig_amount = 0M, cur_amount = 0M;
	int s_index = 0;
	// check if we can put some more uranium into reactors
	var reactors = getReactors();
	for (int i = 0; i < reactors.Count; i++) {
		var reactor = reactors[i] as IMyReactor;
		var rinv = reactor.GetInventory(0);
		Decimal reactor_proportion = (Decimal) reactor.MaxOutput * 1000M / getMaxReactorPowerOutput();
		Decimal reactor_power_draw = getMaxPowerDraw() * (reactor_proportion);
		Decimal ingots_per_reactor = getHighWatermark(reactor_power_draw) / URANIUM_INGOT_POWER;
		Decimal ingots_in_reactor = getTotalIngots(rinv, URANIUM);
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
							ItemHelper tmp = new ItemHelper();
							tmp.Inventory = sinv;
							tmp.Index = j;
							tmp.Item = item;
							ingot = tmp;
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
				rinv.TransferItemFrom(ingot.Value.Inventory, ingot.Value.Index, null, true, null);
				ingot = null;
			} else {
				if (Transfer(ingot.Value.Inventory, rinv, ingot.Value.Index, null, true, (VRage.MyFixedPoint) amount)) {
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

void pushSpareUraniumToStorage() {
	var reactors = getReactors();
	for (int i = 0; i < reactors.Count; i++) {
		var reactor = reactors[i] as IMyReactor;
		var inv = reactor.GetInventory(0);
		if (inv.GetItems().Count > 1) {
			consolidate(inv);
		}
		Decimal ingots = getTotalIngots(reactor.GetInventory(0), URANIUM);
		Decimal reactor_power_draw = getMaxPowerDraw() *
						(((Decimal) reactor.MaxOutput * 1000M) / (getMaxReactorPowerOutput() + getMaxBatteryPowerOutput()));
		Decimal ingots_per_reactor = getHighWatermark(reactor_power_draw);
		if (ingots > ingots_per_reactor) {
			Decimal amount = ingots - ingots_per_reactor;
			pushToStorage(inv, 0, (VRage.MyFixedPoint) amount);
		}
	}
}

/**
 * Trash
 */
bool startThrowing() {
	var connector = getTrashConnector();
	if (connector == null) {
		return false;
	}

	// prepare the connector
	// at this point, we have already turned off the conveyors,
	// so now just check if we aren't throwing anything already and
	// if we aren't in "collect all" mode
	if (connector.CollectAll) {
		// disable collect all
		connector.ApplyAction("CollectAll");
	}

	// if connector is locked, it's in use, so don't do anything
	if (connector.IsLocked) {
		return false;
	}
	if (trashSensorActive()) {
		return false;
	}
	if (!connector.ThrowOut) {
		connector.ApplyAction("ThrowOut");
	}
	return true;
}

void stopThrowing() {
	var connector = getTrashConnector();
	if (connector == null) {
		return;
	}
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

bool trashHasUsefulItems() {
	// check if we have anything other than stone in the connector
	if (getTrashConnector() != null) {
		List < ItemHelper > list = new List < ItemHelper > ();
		var inv = getTrashConnector().GetInventory(0);
		getAllItems(inv, list);
		for (int i = 0; i < list.Count; i++) {
			bool isStone = list[i].Item.Content.SubtypeName == STONE;
			if (!throw_out_stone || !isStone) {
				return true;
			}
		}
	}
	return false;
}

bool trashSensorActive() {
	var sensor = getTrashSensor();
	if (sensor == null) {
		return false;
	}
	return sensor.IsActive;
}

bool throwOutOre(string name) {
	var connector = getTrashConnector();
	if (connector == null) {
		return false;
	}
	var connector_inv = connector.GetInventory(0);
	Decimal orig_target = 5 * CHUNK_SIZE;
	Decimal target_amount = orig_target;

	var entries = getAllOre(name);
	for (int i = 0; i < entries.Count; i++) {
		var entry = entries[i];
		var item = entry.Item;
		var srcInv = entry.Inventory;
		var index = entry.Index;
		var amount = Math.Min(target_amount, (Decimal) entry.Item.Amount);
		// send it to connector
		if (Transfer(srcInv, connector_inv, index, null, true, (VRage.MyFixedPoint) amount)) {
			target_amount -= amount;
			if (target_amount == 0) {
				return true;
			}
		}
	}
	return target_amount != orig_target || entries.Count == 0;
}

void storeTrash() {
	var connector = getTrashConnector();

	if (connector != null) {
		var inv = connector.GetInventory(0);
		consolidate(inv);
		var items = inv.GetItems();
		for (int i = items.Count - 1; i >= 0; i--) {
			var item = items[i];
			pushToStorage(inv, i, null);
			return;
		}
	}
}

/**
 * Ore and refineries
 */
void refineOre() {
	bool alert = false;
	var storage = getStorage();
	for (int i = 0; i < storage.Count; i++) {
		var inv = storage[i].GetInventory(0);
		var items = inv.GetItems();
		for (int j = 0; j < items.Count; j++) {
			List < IMyTerminalBlock > refineries;
			var item = items[j];
			if (!isOre(item)) {
				continue;
			}
			string ore = item.Content.SubtypeName;
			if (ore == SCRAP) {
				ore = IRON;
			}
			if (ore == ICE) {
				refineries = getOxygenGenerators();
				if (refineries.Count == 0) {
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

			Decimal orig_amount = Math.Round((Decimal) item.Amount / (Decimal) refineries.Count, 4);
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
					if (amount < 1) {
						if (Transfer(inv, input_inv, j, input_inv.GetItems().Count, true, null)) {
							break;
						}
					}
					// if refinery is almost empty, send a lot
					else if (input_load < 0.2M) {
						amount = Math.Min(CHUNK_SIZE * 5, orig_amount);
						inv.TransferItemTo(input_inv, j, input_inv.GetItems().Count, true, (VRage.MyFixedPoint) amount);
					} else {
						inv.TransferItemTo(input_inv, j, input_inv.GetItems().Count, true, (VRage.MyFixedPoint) amount);
					}
				}
			}
		}
	}
}

void reprioritizeOre() {
	string low_wm_ore = null;
	string high_wm_ore = null;
	string low_wm_arc_ore = null;
	string high_wm_arc_ore = null;
	// if we know we want uranium, prioritize it
	if (prioritize_uranium && ore_status[URANIUM] > 0) {
		low_wm_ore = URANIUM;
	}
	for (int o = 0; o < ore_types.Count; o++) {
		var ore = ore_types[o];
		if (ore == ICE) {
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
	if (high_wm_ore != null || low_wm_ore != null) {
		var ore = low_wm_ore != null ? low_wm_ore : high_wm_ore;
		List < IMyTerminalBlock > refineries;
		refineries = getRefineries();
		for (int i = 0; i < refineries.Count; i++) {
			var refinery = refineries[i];
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
	if (high_wm_arc_ore != null || low_wm_arc_ore != null) {
		var ore = low_wm_arc_ore != null ? low_wm_arc_ore : high_wm_arc_ore;
		List < IMyTerminalBlock > refineries;
		refineries = getArcFurnaces();
		for (int i = 0; i < refineries.Count; i++) {
			var refinery = refineries[i];
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

public struct RebalanceResult {
	public int minIndex;
	public int maxIndex;
	public Decimal minLoad;
	public Decimal maxLoad;
	public int minArcIndex;
	public int maxArcIndex;
	public Decimal minArcLoad;
	public Decimal maxArcLoad;
}

RebalanceResult findMinMax(List < IMyTerminalBlock > blocks) {
	RebalanceResult r = new RebalanceResult();
	int minI = 0, maxI = 0, minAI = 0, maxAI = 0;
	Decimal minL = Decimal.MaxValue, maxL = 0, minAL = Decimal.MaxValue, maxAL = 0;

	for (int i = 0; i < blocks.Count; i++) {
		var inv = blocks[i].GetInventory(0);
		rebalance(inv);
		Decimal arcload = 0M;
		var items = inv.GetItems();
		for (int j = 0; j < items.Count; j++) {
			var name = items[j].Content.SubtypeName;
			if (name == SCRAP) {
				name = IRON;
			}
			if (arc_furnace_ores.Contains(name)) {
				arcload += (Decimal) items[j].Amount * VOLUME_ORE;
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

bool spreadOre(IMyInventory src_inv, IMyInventory dst_inv) {
	bool success = false;

	var maxLoad = (Decimal) src_inv.CurrentVolume * 1000M;
	var minLoad = (Decimal) dst_inv.CurrentVolume * 1000M;

	var items = src_inv.GetItems();
	var target_volume = (Decimal)(maxLoad - minLoad) / 2M;
	for (int i = items.Count - 1; i >= 0; i--) {
		var volume = items[i].Content.SubtypeName == SCRAP ? VOLUME_SCRAP : VOLUME_ORE;
		var cur_amount = (Decimal) items[i].Amount;
		var cur_vol = (cur_amount * volume) / 2M;
		Decimal amount = Math.Min(target_volume, cur_vol) / volume;
		VRage.MyFixedPoint ? tmp = (VRage.MyFixedPoint) amount;
		if (cur_amount < 250M) {
			tmp = null;
			amount = (Decimal) items[i].Amount;
		}
		if (Transfer(src_inv, dst_inv, i, null, true, tmp)) {
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

void rebalanceRefineries() {
	bool refsuccess = false;
	bool arcsuccess = false;
	var ratio = 1.25M;

	var ogs = getOxygenGenerators();
	RebalanceResult oxyresult = findMinMax(ogs);

	if (oxyresult.maxLoad > 0) {
		bool trySpread = oxyresult.minLoad == 0 || oxyresult.maxLoad / oxyresult.minLoad > ratio;
		if (oxyresult.minIndex != oxyresult.maxIndex && trySpread) {
			var src_inv = ogs[oxyresult.maxIndex].GetInventory(0);
			var dst_inv = ogs[oxyresult.minIndex].GetInventory(0);
			spreadOre(src_inv, dst_inv);
		}
	}

	var refineries = getRefineries();
	var furnaces = getArcFurnaces();
	RebalanceResult refresult = findMinMax(refineries);
	RebalanceResult arcresult = findMinMax(furnaces);

	if (refresult.maxLoad > 250M) {
		bool trySpread = refresult.minLoad == 0 || refresult.maxLoad / refresult.minLoad > ratio;
		if (refresult.minIndex != refresult.maxIndex && trySpread) {
			var src_inv = refineries[refresult.maxIndex].GetInventory(0);
			var dst_inv = refineries[refresult.minIndex].GetInventory(0);
			if (spreadOre(src_inv, dst_inv)) {
				refsuccess = true;
			}
		}
	}
	if (arcresult.maxLoad > 250M) {
		bool trySpread = arcresult.minLoad == 0 || arcresult.maxLoad / arcresult.minLoad > ratio;
		if (arcresult.minIndex != arcresult.maxIndex && trySpread) {
			var src_inv = furnaces[arcresult.maxIndex].GetInventory(0);
			var dst_inv = furnaces[arcresult.minIndex].GetInventory(0);
			if (spreadOre(src_inv, dst_inv)) {
				arcsuccess = true;
			}
		}
	}

	if (refineries.Count == 0 || furnaces.Count == 0 || arcsuccess) {
		return;
	}

	// cross pollination: ref to arc
	Decimal refToArcRatio = 0;
	if (arcresult.minLoad != 0) {
		refToArcRatio = refresult.maxArcLoad / arcresult.minLoad;
	}
	bool refToArc = !refsuccess || (refsuccess && refresult.maxIndex != refresult.maxArcIndex);
	refToArc = refToArcRatio > ratio || (arcresult.minLoad == 0 && refresult.maxArcLoad > 0);
	if (refToArc) {
		var src_inv = refineries[refresult.maxArcIndex].GetInventory(0);
		var dst_inv = furnaces[arcresult.minIndex].GetInventory(0);
		if (spreadOre(src_inv, dst_inv)) {
			return;
		}
	}

	Decimal arcToRefRatio = 0;
	if (refresult.minLoad != 0) {
		arcToRefRatio = arcresult.maxLoad / refresult.minLoad;
	}

	bool arcToRef = refresult.minLoad == 0 || arcToRefRatio > ratio;
	if (arcToRef) {
		var src_inv = furnaces[arcresult.maxIndex].GetInventory(0);
		var dst_inv = refineries[refresult.minIndex].GetInventory(0);
		spreadOre(src_inv, dst_inv);
	}
}

string getBiggestOre() {
	Decimal max = 0;
	string name = "";
	for (int i = 0; i < ore_types.Count; i++) {
		var ore = ore_types[i];
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

bool refineriesClogged() {
	var refineries = getAllRefineries();
	for (int i = 0; i < refineries.Count; i++) {
		var refinery = refineries[i] as IMyRefinery;
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
	for (int i = 0; i < assemblers.Count; i++) {
		var assembler = assemblers[i] as IMyAssembler;
		var inv = assembler.GetInventory(0);

		// empty assembler input if it's not doing anything
		var items = inv.GetItems();
		if (assembler.IsQueueEmpty) {
			items = inv.GetItems();
			for (int j = items.Count - 1; j >= 0; j--) {
				pushToStorage(inv, j, null);
			}
		}

		inv = assembler.GetInventory(1);

		// empty output but only if it's not disassembling
		if (!assembler.DisassembleEnabled) {
			items = inv.GetItems();
			for (int j = items.Count - 1; j >= 0; j--) {
				pushToStorage(inv, j, null);
			}
		}
	}
}

void declogRefineries() {
	var refineries = getAllRefineries();
	for (int i = 0; i < refineries.Count; i++) {
		var refinery = refineries[i] as IMyRefinery;
		var inv = refinery.GetInventory(1);
		var items = inv.GetItems();
		for (int j = items.Count - 1; j >= 0; j--) {
			pushToStorage(inv, j, null);
		}
	}
}

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
	// even out the load between biggest loaded drill
	if (minIndex != maxIndex && (minLoad == 0 || maxLoad / minLoad > 1.1M)) {
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
			if (!Transfer(src_inv, dst_inv, maxIndex, i, true, (VRage.MyFixedPoint) 1)) {
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
	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i] as IMyAirVent;
		StringBuilder builder = new StringBuilder();
		block.GetActionWithName("Depressurize").WriteValue(block, builder);
		if (builder.ToString() == "Off" && !block.IsPressurized()) {
			addBlockAlert(block, ALERT_OXYGEN_LEAK);
			alert = true;
		} else {
			removeBlockAlert(block, ALERT_OXYGEN_LEAK);
		}
		displayBlockAlerts(block);
	}
	if (alert) {
		addAlert(BROWN_ALERT);
	} else {
		removeAlert(BROWN_ALERT);
	}
}

/**
 * Functions pertaining to BARABAS's operation
 */
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
	oxygen_threshold = has_oxygen_tanks ? 15M : 0M;
	hydrogen_threshold = 0M;
}

// update defaults based on auto configured values
void autoConfigure() {
	resetConfig();
	if (op_mode == OP_MODE_BASE) {
		sort_storage = true;
	} else if (op_mode == OP_MODE_DRILL) {
		push_ore_to_base = true;
		if (can_refine) {
			push_ingots_to_base = true;
		}
	} else if (op_mode == OP_MODE_GRINDER) {
		push_components_to_base = true;
	}
}

void configureWatermarks() {
	if (!has_reactors) {
		auto_refuel_ship = false;
	} else {
		auto_refuel_ship = true;
	}
	if (power_low_watermark == 0) {
		if (op_mode == OP_MODE_BASE) {
			power_low_watermark = 60;
		} else {
			power_low_watermark = 15;
		}
	}
	if (power_high_watermark == 0) {
		if (op_mode == OP_MODE_BASE) {
			power_high_watermark = 480;
		} else {
			power_high_watermark = 120;
		}
	}
}

// select operation mode, unless already set in config
void selectOperationMode() {
	var list = new List < IMyTerminalBlock > ();
	var wlist = new List < IMyTerminalBlock > ();
	// get a list of local thrusters and wheels
	GridTerminalSystem.GetBlocksOfType < IMyThrust > (list, localGridDumbFilter);
	GridTerminalSystem.GetBlocksOfType < IMyMotorSuspension > (wlist, localGridDumbFilter);
	// if we found some thrusters or wheels, assume we're a ship
	if (list.Count > 0 || wlist.Count > 0) {
		// this is likely a drill ship
		if (has_drills && !has_welders && !has_grinders) {
			op_mode = OP_MODE_DRILL;
		}
		// this is likely a welder ship
		else if (has_welders && !has_drills && !has_grinders) {
			op_mode = OP_MODE_WELDER;
		}
		// this is likely a grinder ship
		else if (has_grinders && !has_drills && !has_welders) {
			op_mode = OP_MODE_GRINDER;
		}
		// we don't know what the hell this is, so don't adjust the defaults
		else {
			op_mode = OP_MODE_SHIP;
		}
	} else {
		op_mode = OP_MODE_BASE;
	}
}

void addAlert(int level) {
	var alert = text_alerts[level];
	// this alert is already enabled
	if (alert.enabled) {
		return;
	}
	alert.enabled = true;
	text_alerts[level] = alert;
	Alert ? cur_alert = null;
	string text = "";

	removeAntennaAlert(ALERT_LOW_POWER);
	removeAntennaAlert(ALERT_LOW_STORAGE);
	removeAntennaAlert(ALERT_VERY_LOW_STORAGE);
	removeAntennaAlert(ALERT_MATERIAL_SHORTAGE);

	// now, find enabled alerts
	for (int i = 0; i < text_alerts.Count; i++) {
		alert = text_alerts[i];
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
			if (cur_alert == null) {
				cur_alert = alert;
				text += alert.text;
			} else {
				text += ", " + alert.text;
			}
		}
	}
	displayAntennaAlerts();
	showAlertColor(cur_alert.Value.color);
	status_report[STATUS_ALERT] = text;
}

void removeAlert(int level) {
	// disable the alert
	var old_alert = text_alerts[level];
	old_alert.enabled = false;
	text_alerts[level] = old_alert;
	// now, see if we should display another alert
	Nullable < Alert > alert = null;
	string text = "";

	removeAntennaAlert(ALERT_LOW_POWER);
	removeAntennaAlert(ALERT_LOW_STORAGE);
	removeAntennaAlert(ALERT_VERY_LOW_STORAGE);
	removeAntennaAlert(ALERT_MATERIAL_SHORTAGE);

	// now, find enabled alerts
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
				text += alert.Value.text;
			} else {
				text += ", " + text_alerts[i].text;
			}
		}
	}
	status_report[STATUS_ALERT] = text;
	if (!alert.HasValue) {
		hideAlertColor();
	} else {
		showAlertColor(alert.Value.color);
	}
	displayAntennaAlerts();
}

bool clStrCompare(string str1, string str2) {
	return String.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
}

string generateConfiguration() {
	StringBuilder sb = new StringBuilder();

	if (op_mode == OP_MODE_BASE) {
		config_options[CONFIGSTR_OP_MODE] = "base";
	} else if (op_mode == OP_MODE_SHIP) {
		config_options[CONFIGSTR_OP_MODE] = "ship";
	} else if (op_mode == OP_MODE_DRILL) {
		config_options[CONFIGSTR_OP_MODE] = "drill";
	} else if (op_mode == OP_MODE_WELDER) {
		config_options[CONFIGSTR_OP_MODE] = "welder";
	} else if (op_mode == OP_MODE_GRINDER) {
		config_options[CONFIGSTR_OP_MODE] = "grinder";
	} else if (op_mode == OP_MODE_TUG) {
		config_options[CONFIGSTR_OP_MODE] = "tug";
	}
	config_options[CONFIGSTR_HUD_NOTIFICATIONS] = Convert.ToString(hud_notifications);
	config_options[CONFIGSTR_POWER_HIGH_WATERMARK] = Convert.ToString(power_high_watermark);
	config_options[CONFIGSTR_POWER_LOW_WATERMARK] = Convert.ToString(power_low_watermark);
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
			config_options[CONFIGSTR_KEEP_STONE] = Convert.ToString(Math.Floor((material_thresholds[STONE] * 5) / CHUNK_SIZE));
		}
	} else {
		config_options[CONFIGSTR_KEEP_STONE] = "all";
	}
	if (oxygen_threshold != 0M) {
		config_options[CONFIGSTR_OXYGEN_THRESHOLD] = Convert.ToString(oxygen_threshold);
	} else {
		config_options[CONFIGSTR_OXYGEN_THRESHOLD] = "none";
	}
	if (hydrogen_threshold != 0M) {
		config_options[CONFIGSTR_HYDROGEN_THRESHOLD] = Convert.ToString(hydrogen_threshold);
	} else {
		config_options[CONFIGSTR_HYDROGEN_THRESHOLD] = "none";
	}

	// currently selected operation mode
	sb.AppendLine("# Operation mode");
	sb.AppendLine("# Can be auto, base, ship, tug, drill, welder or grinder");
	var key = CONFIGSTR_OP_MODE;
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_HUD_NOTIFICATIONS;
	sb.AppendLine("# HUD notifications for blocks and antennas.");
	sb.AppendLine("# Can be True or False.");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_POWER_HIGH_WATERMARK;
	sb.AppendLine("# Amount of power on \"full\" batteries/reactors, in minutes.");
	sb.AppendLine("# Can be a positive number, zero for automatic.");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_POWER_LOW_WATERMARK;
	sb.AppendLine("# Amount of power on \"empty\" batteries/reactors, in minutes.");
	sb.AppendLine("# Can be a positive number, zero for automatic.");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_OXYGEN_THRESHOLD;
	sb.AppendLine("# Percentage of oxygen to be considered \"enough\".");
	sb.AppendLine("# Can be a number between 0 and 100, or \"none\" to disable.");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_HYDROGEN_THRESHOLD;
	sb.AppendLine("# Percentage of hydrogen to be considered \"enough\".");
	sb.AppendLine("# Can be a number between 0 and 100, or \"none\" to disable.");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_KEEP_STONE;
	sb.AppendLine("# How much stone to keep, in tons.");
	sb.AppendLine("# Can be a positive number, \"none\", \"all\" or \"auto\".");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_SORT_STORAGE;
	sb.AppendLine("# Automatically sort items in storage containers.");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	sb.AppendLine("#");
	sb.AppendLine("# Values below this line are only applicable to");
	sb.AppendLine("# ships when connected to base or other ships.");
	sb.AppendLine("#");
	sb.AppendLine();
	key = CONFIGSTR_PUSH_ORE;
	sb.AppendLine("# Push ore to base storage");
	sb.AppendLine("# In tug mode, also pull ore from ships");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_PUSH_INGOTS;
	sb.AppendLine("# Push ingots to base storage");
	sb.AppendLine("# In tug mode, also pull ingots from ships");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_PUSH_COMPONENTS;
	sb.AppendLine("# Push components to base storage");
	sb.AppendLine("# In tug mode, also pull components from ships");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_PULL_ORE;
	sb.AppendLine("# Pull ore from base storage");
	sb.AppendLine("# In tug mode, also push ore to ships");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_PULL_INGOTS;
	sb.AppendLine("# Pull ingots from base storage");
	sb.AppendLine("# In tug mode, also push ingots to ships");
	sb.AppendLine("# Can be True or False");
	sb.AppendLine(key + " = " + config_options[key]);
	sb.AppendLine();
	key = CONFIGSTR_PULL_COMPONENTS;
	sb.AppendLine("# Pull components from base storage");
	sb.AppendLine("# In tug mode, also push components to ships");
	sb.AppendLine("# Can be True or False");
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
	block.WritePublicText(text);
	block.WritePublicTitle("BARABAS Configuration");
}

void parseLine(string line) {
	string[] strs = line.Split('=');
	if (strs.Length != 2) {
		throw new BarabasException("Invalid number of tokens: " + line);
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
	if (str == "reactor low watermark") {
		str = CONFIGSTR_POWER_LOW_WATERMARK;
	}
	if (str == "reactor high watermark") {
		str = CONFIGSTR_POWER_HIGH_WATERMARK;
	}
	if (!config_options.ContainsKey(str)) {
		throw new BarabasException("Invalid config option: " + str);
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
			if (op_mode != OP_MODE_BASE) {
				op_mode = OP_MODE_BASE;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "ship") {
			if (op_mode != OP_MODE_SHIP) {
				op_mode = OP_MODE_SHIP;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "drill") {
			if (op_mode != OP_MODE_DRILL) {
				op_mode = OP_MODE_DRILL;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "welder") {
			if (op_mode != OP_MODE_WELDER) {
				op_mode = OP_MODE_WELDER;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "grinder") {
			if (op_mode != OP_MODE_GRINDER) {
				op_mode = OP_MODE_GRINDER;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "tug") {
			if (op_mode != OP_MODE_TUG) {
				op_mode = OP_MODE_TUG;
				crisis_mode = CRISIS_MODE_NONE;
			}
		} else if (strval == "auto") {
			op_mode = OP_MODE_AUTO;
			crisis_mode = CRISIS_MODE_NONE;
		} else {
			fail = true;
		}
	} else if (clStrCompare(str, CONFIGSTR_POWER_HIGH_WATERMARK)) {
		if (fparse && fval >= 0) {
			power_high_watermark = fval;
		} else {
			fail = true;
		}
	} else if (clStrCompare(str, CONFIGSTR_POWER_LOW_WATERMARK)) {
		if (fparse && fval >= 0) {
			power_low_watermark = fval;
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
	} else if (clStrCompare(str, CONFIGSTR_OXYGEN_THRESHOLD)) {
		if (fparse && fval >= 0 && fval <= 100) {
			oxygen_threshold = fval;
		} else if (strval == "none") {
			oxygen_threshold = 0;
		} else {
			fail = true;
		}
	} else if (clStrCompare(str, CONFIGSTR_HYDROGEN_THRESHOLD)) {
		if (fparse && fval >= 0 && fval <= 100) {
			hydrogen_threshold = fval;
		} else if (strval == "none") {
			hydrogen_threshold = 0;
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
		throw new BarabasException("Invalid config value: " + strval);
	}
}

// this will find a BARABAS Config block and read its configuration
void parseConfiguration() {
	// find the block, blah blah
	var block = getConfigBlock();
	if (block == null) {
		return;
	}
	string text = block.GetPublicText();

	// check if the text is empty
	if (text.Trim().Length != 0) {
		var lines = text.Split('\n');
		for (int i = 0; i < lines.Length; i++) {
			var line = lines[i].Trim();

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
	for (int i = 0; i < antennas.Count; i++) {
		var antenna = antennas[i];
		displayBlockAlerts(antenna);
	}
}

void addAntennaAlert(int id) {
	if (!hud_notifications) {
		return;
	}
	var antennas = getAntennas();
	for (int i = 0; i < antennas.Count; i++) {
		var antenna = antennas[i];
		addBlockAlert(antenna, id);
	}
}

void removeAntennaAlert(int id) {
	if (!hud_notifications) {
		return;
	}
	var antennas = getAntennas();
	for (int i = 0; i < antennas.Count; i++) {
		var antenna = antennas[i];
		removeBlockAlert(antenna, id);
	}
}

void showAlertColor(Color c) {
	var lights = getLights();
	for (int i = 0; i < lights.Count; i++) {
		var light = lights[i] as IMyLightingBlock;
		if (light.GetValue < Color > ("Color").Equals(c) && light.Enabled) {
			continue;
		}
		light.SetValue("Color", c);
		// make sure we switch the color of the texture as well
		light.ApplyAction("OnOff_Off");
		light.ApplyAction("OnOff_On");
	}
}

void hideAlertColor() {
	var lights = getLights();
	for (int i = 0; i < lights.Count; i++) {
		var light = lights[i] as IMyLightingBlock;
		if (!light.Enabled) {
			continue;
		}
		light.ApplyAction("OnOff_Off");
	}
}

void turnOffConveyors() {
	var blocks = getBlocks();
	// go through all blocks and set "use conveyor" to off
	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
		if (block is IMyAssembler) {
			continue;
		}
		if (block is IMyShipWelder) {
			continue;
		}
		if (block.HasAction("UseConveyor") && block.GetUseConveyorSystem()) {
			block.ApplyAction("UseConveyor");
		}
	}
}

void displayStatusReport() {
	var panels = getTextPanels();

	if (crisis_mode == CRISIS_MODE_NONE && tried_throwing) {
		status_report[STATUS_CRISIS_MODE] = "Standby";
	} else if (crisis_mode == CRISIS_MODE_NONE) {
		status_report[STATUS_CRISIS_MODE] = "";
	} else if (crisis_mode == CRISIS_MODE_THROW_ORE) {
		status_report[STATUS_CRISIS_MODE] = "Ore throwout";
	} else if (crisis_mode == CRISIS_MODE_LOCKUP) {
		status_report[STATUS_CRISIS_MODE] = "Lockup";
	}

	// construct panel text
	string panel_text = "";
	Dictionary < string, string > .Enumerator e;
	e = status_report.GetEnumerator();
	while (e.MoveNext()) {
		var item = e.Current;
		if (item.Value == "") {
			continue;
		}
		panel_text += item.Key + ": " + item.Value + "\n";
	}
	for (int i = 0; i < panels.Count; i++) {
		var panel = panels[i] as IMyTextPanel;
		panel.WritePublicText(panel_text);
		panel.WritePublicTitle("BARABAS Notify Report");
		panel.ShowTextureOnScreen();
		panel.ShowPublicTextOnScreen();
	}
}

/*
 * States
 */
void s_refreshState() {
	getLocalGrids(true);
	getBlocks(true);
	getConfigBlock(true);
	has_refineries = getRefineries(true).Count > 0;
	has_arc_furnaces = getArcFurnaces(true).Count > 0;
	can_refine = has_refineries || has_arc_furnaces || (getOxygenGenerators(true).Count > 0);
	can_use_ingots = getAssemblers(true).Count > 0;
	has_reactors = getReactors(true).Count > 0;
	has_air_vents = getAirVents(true).Count > 0;
	has_oxygen_tanks = getOxygenTanks(true).Count > 0;
	has_hydrogen_tanks = getHydrogenTanks(true).Count > 0;
	has_connectors = getConnectors(true).Count > 0;
	has_single_connector = getConnectors().Count == 1;
	has_trash_sensor = getTrashSensor(true) != null;
	has_drills = getDrills(true).Count > 0;
	has_grinders = getGrinders(true).Count > 0;
	has_welders = getWelders(true).Count > 0;
	has_status_panels = getTextPanels(true).Count > 0;
	can_use_oxygen = has_oxygen_tanks && has_air_vents;
	getBatteries(true);
	getTrashConnector(true);
	getStorage(true);
	getLights(true);
	getAntennas(true);
	if (has_reactors) {
		getMaxReactorPowerOutput(true);
		getCurReactorPowerOutput(true);
	}
	getMaxBatteryPowerOutput(true);
	getCurPowerDraw(true);
	getMaxPowerDraw(true);
	if (!has_single_connector) {
		startThrowing();
	}
	turnOffConveyors();

	// configure BARABAS
	parseConfiguration();
	if (op_mode == OP_MODE_AUTO) {
		selectOperationMode();
		autoConfigure();
	}
	if ((op_mode & OP_MODE_SHIP) > 0) {
		Me.SetCustomName("BARABAS Ship CPU");
	} else {
		Me.SetCustomName("BARABAS Base CPU");
	}
	configureWatermarks();
	rebuildConfiguration();


	if (pull_ingots_from_base && push_ingots_to_base) {
		throw new BarabasException("Invalid configuration - " +
			"pull_ingots_from_base and push_ingots_to_base both set to \"true\"");
	}
	if (pull_ore_from_base && push_ore_to_base) {
		throw new BarabasException("Invalid configuration - " +
			"pull_ore_from_base and push_ore_to_base both set to \"true\"");
	}
	if (pull_components_from_base && push_components_to_base) {
		throw new BarabasException("Invalid configuration - " +
			"pull_components_from_base and push_components_to_base both set to \"true\"");
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
	bool above_high_watermark = aboveHighWatermark();
	var max_pwr_output = getCurReactorPowerOutput() + getMaxBatteryPowerOutput();

	// if we have enough uranium ingots, business as usual
	if (!above_high_watermark) {
		// check if we're below low watermark
		bool above_low_watermark = aboveLowWatermark();

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
		if ((op_mode & OP_MODE_SHIP) > 0 && connected_to_base) {
			time = Math.Round(stored_power / max_pwr_draw, 0);
		} else {
			time = Math.Round(stored_power / adjusted_pwr_draw, 0);
		}
		if (time > 300) {
			time = Math.Floor(time / 60M);
			if (time > 48) {
				time = Math.Floor(time / 24M);
				time_str = Convert.ToString(time) + " d";
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
		bool can_refuel = (op_mode & OP_MODE_SHIP) > 0 && connected_to_base;
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
		if ((op_mode & OP_MODE_SHIP) > 0 && push_ore_to_base && connected_to_base) {
			pushOreToStorage();
		} else {
			refineOre();
		}
	}
}

void s_materials() {
	// check if any ore needs to be prioritized
	if (can_use_ingots || prioritize_uranium) {
		reprioritizeOre();
	}
	if (can_refine) {
		rebalanceRefineries();
	}
	if (crisis_mode == CRISIS_MODE_NONE && throw_out_stone) {
		// check if we want to throw out extra stone
		if (ore_status[STONE] > 0) {
			if (has_refineries) {
				bool haveEnoughStone = (ingot_status[STONE] + storage_ore_status[STONE] * ore_to_ingot_ratios[STONE]) > (material_thresholds[STONE] * 5);
				if (haveEnoughStone) {
					// prevent us from throwing out something of value
					if (!trashHasUsefulItems() && startThrowing()) {
						throwOutOre(STONE);
					}
				}
			} else {
				if (ore_status[STONE] > material_thresholds[STONE] * 5) {
					if (startThrowing()) {
						throwOutOre(STONE);
					}
				} else {
					storeTrash();
				}
			}
		}
	} else if (crisis_mode == CRISIS_MODE_THROW_ORE) {
		tried_throwing = true;
		// if we can't even throw out ore, well, all bets are off
		string ore = getBiggestOre();
		if ((ore != null && startThrowing() && !throwOutOre(ore)) || ore == null) {
			stopThrowing();
			crisis_mode = CRISIS_MODE_LOCKUP;
		}
	}
}

void s_tools() {
	var drills = getDrills();
	var grinders = getGrinders();
	if (has_drills) {
		emptyBlocks(drills);
		spreadLoad(drills);
	}
	if (has_grinders) {
		emptyBlocks(grinders);
		spreadLoad(grinders);
	}
	if (has_welders && op_mode == OP_MODE_WELDER) {
		fillWelders();
	}
}

void s_storage() {
	if (can_use_ingots) {
		declogAssemblers();
	}
	if (can_refine) {
		declogRefineries();
	}
	if (sort_storage) {
		sortLocalStorage();
	}
	if ((op_mode & OP_MODE_SHIP) > 0 && connected_to_base) {
		pushAllToRemoteStorage();
		pullFromRemoteStorage();
	}
	// tug is a special case as it can push to and pull from ships, but only
	// when connected to a ship and not to a base
	else if (op_mode == OP_MODE_TUG && connected_to_ship && !connected_to_base) {
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
	for (int i = 0; i < ore_types.Count; i++) {
		ore_status[ore_types[i]] = 0;
		storage_ore_status[ore_types[i]] = 0;
		ingot_status[ore_types[i]] = 0;
		storage_ingot_status[ore_types[i]] = 0;
	}
	var blocks = getBlocks();
	for (int b = 0; b < blocks.Count; b++) {
		var block = blocks[b];
		for (int i = 0; i < block.GetInventoryCount(); i++) {
			var inv = block.GetInventory(i);
			var items = inv.GetItems();
			for (int j = 0; j < items.Count; j++) {
				var item = items[j];
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
	StringBuilder sb = new StringBuilder();
	for (int i = 0; i < ore_types.Count; i++) {
		string ore = ore_types[i];
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
			if (op_mode != OP_MODE_BASE && total == 0) {
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
		for (int i = 0; i < tanks.Count; i++) {
			var tank = tanks[i] as IMyOxygenTank;
			oxy_cur += (Decimal) tank.GetOxygenLevel();
			oxy_total += 1M;
		}
		tanks = getHydrogenTanks();
		for (int i = 0; i < tanks.Count; i++) {
			var tank = tanks[i] as IMyOxygenTank;
			hydro_cur += (Decimal) tank.GetOxygenLevel();
			hydro_total += 1M;
		}
		Decimal oxy_p = has_oxygen_tanks ? (oxy_cur / oxy_total) * 100M : 0;
		Decimal hydro_p = has_hydrogen_tanks ? (hydro_cur / hydro_total) * 100M : 0;
		string oxy_str = !has_oxygen_tanks ? "N/A" : String.Format("{0:0.0}%",
					oxy_p);
		string hydro_str = !has_hydrogen_tanks ? "N/A" : String.Format("{0:0.0}%",
					hydro_p);
		if (oxygen_threshold > 0 && oxy_p < oxygen_threshold) {
			alert = true;
			addAntennaAlert(ALERT_LOW_OXYGEN);
		} else {
			removeAntennaAlert(ALERT_LOW_OXYGEN);
		}
		if (hydrogen_threshold > 0 && hydro_p < hydrogen_threshold) {
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
	}
}

int[] state_cycle_counts;
int[] state_fn_counts;
int cycle_count;
int fn_count;

// saving state
public void Save() {
	saveLocalGrids(local_grids);
}

bool canContinue() {
	bool hasHeadroom = false;
	bool isFirstRun = state_cycle_counts[current_state] == 0;
	var prev_state = current_state == 0 ? states.Length - 1 : current_state - 1;
	var next_state = (current_state + 1) % states.Length;
	var cur_i = Runtime.CurrentInstructionCount;
	var cur_fn = Runtime.CurrentMethodCallCount;

	// now store how many cycles we've used during this iteration
	state_cycle_counts[current_state] = cur_i - cycle_count;
	state_fn_counts[current_state] = cur_fn - fn_count;

	var last_cycle_count = state_cycle_counts[next_state];
	var last_fn_count = state_fn_counts[next_state];

	// if we have enough headroom (we want no more than 80% cycle/method count)
	int projected_cycle_count = cur_i + last_cycle_count;
	int projected_fn_count = cur_fn + last_fn_count;
	Decimal cycle_percentage = (Decimal) projected_cycle_count / Runtime.MaxInstructionCount;
	Decimal fn_percentage = (Decimal) projected_fn_count / Runtime.MaxMethodCallCount;

	if (!isFirstRun && last_cycle_count != 0 && last_fn_count != 0 &&
			cycle_percentage <= 0.8M && fn_percentage <= 0.8M) {
		hasHeadroom = true;
	}

	// advance current state and store IL count values
	current_state = next_state;
	cycle_count = cur_i;
	fn_count = cur_fn;

	return hasHeadroom;
}

// constructor
public Program() {
	// kick off state machine
	states = new Action [] {
		s_refreshState,
		s_refreshRemote,
		s_updateMaterialStats,
		s_power,
		s_refineries,
		s_materials,
		s_tools,
		s_storage
	};
	current_state = 0;
	crisis_mode = CRISIS_MODE_NONE;
	state_cycle_counts = new int[states.Length];
	state_fn_counts = new int[states.Length];

	for (int i = 0; i < state_cycle_counts.Length; i++) {
		state_cycle_counts[i] = 0;
		state_fn_counts[i] = 0;
	}

	// load grids from storage
	local_grids = new List < IMyCubeGrid > ();
	loadLocalGrids(local_grids);
}

public void Main() {
	int num_states = 0;
	cycle_count = 0;
	fn_count = 0;
	do {
		states[current_state]();
		num_states++;
	} while (canContinue() && num_states < states.Length);

	if (has_single_connector && trashSensorActive()) {
		stopThrowing();
		storeTrash();
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
	string fn_str = String.Format("Call count: {0}/{1} ({2:0.0}%)",
				Runtime.CurrentMethodCallCount,
				Runtime.MaxMethodCallCount,
				(Decimal) Runtime.CurrentMethodCallCount / Runtime.MaxMethodCallCount * 100M);
	Echo(String.Format("States executed: {0}", num_states));
	Echo(il_str);
	Echo(fn_str);
}
