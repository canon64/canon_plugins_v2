using MainGameUiHelp;
using Mono.Cecil;
using System.Text.Json;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}

var data = JsonSerializer.Deserialize<HelpCatalogFile>(File.ReadAllText(Path.Combine(root, "help_catalog.json")),
    new JsonSerializerOptions { IncludeFields = true });
var catalog = new HelpCatalog();
var keys = new HashSet<string>();
foreach (var entry in data.entries)
{
    Check(!string.IsNullOrWhiteSpace(entry.text), "Empty explanation: " + entry.label);
    Check(keys.Add(entry.owner + "\n" + HelpCatalog.Normalize(entry.label)), "Duplicate normalized key: " + entry.owner + "/" + entry.label);
    catalog.Add(entry.owner, entry.label, entry.text);
}

using (var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ui_inventory.json"))))
{
    foreach (var owner in inventory.RootElement.EnumerateObject())
        foreach (var control in owner.Value.EnumerateArray())
        {
            string label = control.GetProperty("label").GetString();
            if (HelpCatalog.Normalize(label).Length == 0) continue;
            Check(catalog.Find(owner.Name, label) != null, "Missing UI explanation: " + owner.Name + "/" + label);
        }
}
string cap = catalog.Find("MainGameVrDildo", "深い一撃\n距離上限の挿入路割合");
Check(cap != null && cap.Contains("13 cm") && cap.Contains("10 cm"), "Deep distance cap needs both shorter-of-two examples.");
Check(catalog.Find("MainGameVrDildo", "深い一撃の到達率").Contains("14 cm"), "Depth threshold needs its separate example.");
Check(catalog.Find("MainGameObjectComposer", "他").Contains("O"), "Other lane must explain O.");
Check(catalog.Find("MainGameClothColliderCards", "X").Contains("削除"), "Cloth X is delete.");
Check(!catalog.Find("MainGameObjectComposer", "X").Contains("削除"), "Composer X is not delete.");
Check(catalog.Find("MainGameFaceSweatControl", "Face Sweat Level: 0.5") != null, "Value suffix lookup.");
Check(HelpCatalog.Range(.65f, .1f, 1f).Contains("0.65"), "Range formatting.");
Check(HelpCatalog.IsSameRow(0, 160, 10, 46, 166, 10, 26), "Two-line label + slider.");
Check(!HelpCatalog.IsSameRow(0, 160, 10, 30, 166, 35, 55), "Do not borrow help from another row.");
Check(!HelpCatalog.IsSameRow(0, 160, 10, 30, 20, 10, 30), "Do not borrow from a control to the left.");

// Inspect the actual Unity 2019 binary: these hooks must sit on managed paths
// reached by both GUILayout and GUI controls. This does not execute Unity.
using var unity = ModuleDefinition.ReadModule(@"F:\kks\KoikatsuSunshine_Data\Managed\UnityEngine.IMGUIModule.dll");
var gui = unity.GetType("UnityEngine.GUI");
var hooks = new[] { ("DoLabel", 3), ("DoButton", 4), ("DoToggle", 5), ("DoTextField", 8), ("Slider", 10), ("DoButtonGrid", 11) };
foreach (var hook in hooks)
{
    var targets = gui.Methods.Where(m => m.Name == hook.Item1 && m.Parameters.Count == hook.Item2).ToArray();
    Check(targets.Length == 1 && targets[0].HasBody && targets[0].IsStatic, "Unsupported Unity hook: " + hook);
}
var clip = unity.GetType("UnityEngine.GUIClip");
Check(clip.Methods.Any(m => m.Name == "get_visibleRect" && m.IsStatic && m.ReturnType.FullName == "UnityEngine.Rect"), "Clip visibility getter.");

bool Reaches(MethodDefinition method, string goal, HashSet<string> visited)
{
    if (method.Name == goal && method.DeclaringType.FullName == "UnityEngine.GUI"
        && (goal != "DoTextField" || method.Parameters.Count == 8)) return true;
    if (!visited.Add(method.FullName) || !method.HasBody) return false;
    foreach (var instruction in method.Body.Instructions)
        if (instruction.Operand is MethodReference reference)
        {
            var type = unity.GetType(reference.DeclaringType.FullName);
            if (type == null) continue;
            var next = type.Methods.FirstOrDefault(m => m.FullName == reference.FullName);
            if (next != null && Reaches(next, goal, visited)) return true;
        }
    return false;
}
foreach (string api in new[] { "UnityEngine.GUI", "UnityEngine.GUILayout" })
    foreach (var control in new[] { ("Label", "DoLabel"), ("Button", "DoButton"), ("Toggle", "DoToggle"), ("HorizontalSlider", "Slider"), ("TextField", "DoTextField") })
    {
        var methods = unity.GetType(api).Methods.Where(m => m.Name == control.Item1 && m.IsPublic && m.IsStatic).ToArray();
        Check(methods.Length > 0, "Missing API: " + api + "/" + control);
        foreach (var method in methods)
            Check(Reaches(method, control.Item2, new HashSet<string>()), "Unobserved control overload: " + method.FullName);
    }

using var plugin = ModuleDefinition.ReadModule(Path.Combine(root, "bin", "Release", "net472", "MainGameUiHelp.dll"));
var pluginType = plugin.GetType("MainGameUiHelp.Plugin");
var positions = new Dictionary<string, (string Target, int Count)>
{
    ["LabelPrefix"] = ("DoLabel", 3), ["ButtonPrefix"] = ("DoButton", 4),
    ["TogglePrefix"] = ("DoToggle", 5), ["TextPrefix"] = ("DoTextField", 8),
    ["SliderPrefix"] = ("Slider", 10), ["GridPrefix"] = ("DoButtonGrid", 11)
};
foreach (var pair in positions)
{
    var prefix = pluginType.Methods.Single(m => m.Name == pair.Key);
    var original = gui.Methods.Single(m => m.Name == pair.Value.Target && m.Parameters.Count == pair.Value.Count);
    Check(prefix.ReturnType.FullName == "System.Void", "Observation must not cancel a control.");
    foreach (var parameter in prefix.Parameters)
    {
        Check(!parameter.ParameterType.IsByReference, "Observation must not replace control values.");
        int index = int.Parse(parameter.Name.Substring(2));
        Check(parameter.ParameterType.FullName == original.Parameters[index].ParameterType.FullName, "Hook parameter mismatch: " + pair.Key + "/" + parameter.Name);
    }
}
var calls = pluginType.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
    .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.FullName).ToArray();
Check(!calls.Any(m => m.Contains("UnityEngine.Event::Use") || m.Contains("set_hotControl") || m.Contains("Application::Quit")), "Help must not consume input or control the game.");
Console.WriteLine($"PASS: {assertions} checks; {catalog.Count} unique help entries; actual Unity control paths and prefix signatures verified. In-game rendering remains untested.");
