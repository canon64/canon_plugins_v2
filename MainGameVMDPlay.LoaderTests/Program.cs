using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
class Program {
 static void Main() {
  AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
   string name = new AssemblyName(e.Name).Name + ".dll";
   foreach (string dir in new[]{@"F:\kks\KoikatsuSunshine_Data\Managed", @"F:\kks\BepInEx\core", @"F:\kks\work\plugins\canon_plugins\MainGameVMDPlay\bin\Release\net472"}) {
    string path = Path.Combine(dir,name); if(File.Exists(path)) return Assembly.LoadFrom(path);
   }
   string found=Directory.EnumerateFiles(@"F:\kks\BepInEx\plugins",name,SearchOption.AllDirectories).FirstOrDefault();
   return found==null?null:Assembly.LoadFrom(found);
  };
  Verify();
 }
 static void Verify() {
  var game=Assembly.LoadFrom(@"F:\kks\KoikatsuSunshine_Data\Managed\Assembly-CSharp.dll");
  var plugin=Assembly.LoadFrom(@"F:\kks\work\plugins\canon_plugins\MainGameVMDPlay\bin\Release\net472\MainGameVMDPlay.dll");
  var abmx=Assembly.LoadFrom(@"F:\kks\BepInEx\plugins\KKSABMX.dll").GetType("KKABMX.Core.BoneController",true);
  bool ambiguous=false;
  try { AccessTools.Method(abmx,"OnReload"); } catch(AmbiguousMatchException) { ambiguous=true; }
  if(!ambiguous) throw new Exception("expected original ABMX lookup ambiguity not reproduced");
  var resolve=plugin.GetType("MainGameVMDPlay.Plugin").GetMethod("ResolveActorAbmxReload",BindingFlags.Static|BindingFlags.NonPublic);
  var reload=(MethodInfo)resolve.Invoke(null,new object[]{abmx});
  if(reload.DeclaringType!=abmx || reload.GetParameters().Length!=2 || reload.GetParameters()[1].ParameterType!=typeof(bool)) throw new Exception("ABMX reload resolution invalid");
  Console.WriteLine("PASS ABMX: original ambiguity reproduced; production resolver selects declared two-argument override");
  var skinAssembly=Assembly.LoadFrom(@"F:\kks\BepInEx\plugins\KKS_SkinEffects.dll");
  var skin=skinAssembly.GetType("KK_SkinEffects.SkinEffectsController",true);
  var texture=skinAssembly.GetType("KK_SkinEffects.TextureLoader",true);
  var production=plugin.GetType("MainGameVMDPlay.Plugin",true);
  var properties=(string[])production.GetField("ActorModProperties",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  var counts=(string[])production.GetField("ActorModCounts",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  if(properties.Length!=11 || properties.Length!=counts.Length)throw new Exception("skin channel inventory incomplete");
  for(int i=0;i<properties.Length;i++) {
   var property=skin.GetProperty(properties[i]);
   var count=texture.GetProperty(counts[i],BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
   if(property==null || !property.CanWrite || property.PropertyType!=typeof(int) || count==null || count.PropertyType!=typeof(int))throw new Exception("skin contract "+properties[i]);
  }
  Console.WriteLine("PASS installed SkinEffects: all 11 production channels and texture-count properties");
  var loader=plugin.GetType("MainGameVMDPlay.OwnedCharacterLoader",true);
  var transpile=loader.GetMethod("Transpile",BindingFlags.Static|BindingFlags.NonPublic);
  int total=0;
  foreach(string name in new[]{"LoadAsync","LoadCharaFbxDataAsync"}) {
   var native=game.GetType("ChaControl").GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
   var state=native.GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType;
   var move=state.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
   var original=PatchProcessor.GetOriginalInstructions(move);
   var rewritten=((IEnumerable<CodeInstruction>)transpile.Invoke(null,new object[]{original,move})).ToList();
   int expected=name=="LoadAsync"?7:1, count=0;
   for(int i=0;i<rewritten.Count;i++) {
    var call=rewritten[i].operand as MethodInfo;
    if(call==null || call.DeclaringType!=loader || call.Name!="Load")continue;
    if(i<2 || rewritten[i-2].opcode!=OpCodes.Ldarg_0 || rewritten[i-1].opcode!=OpCodes.Ldfld || ((FieldInfo)rewritten[i-1].operand).FieldType!=game.GetType("ChaControl")) throw new Exception("owner stack invalid");
    if(call.GetParameters().Length!=6 || call.GetParameters()[5].ParameterType!=game.GetType("ChaControl"))throw new Exception("wrapper signature invalid");
    count++;
   }
   if(count!=expected || rewritten.Any(c => c.operand is MethodInfo m && m.Name=="Load_Coroutine"))throw new Exception("replacement count mismatch");
   var labels=new HashSet<Label>(rewritten.SelectMany(c=>c.labels));
   foreach(var c in rewritten) { if(c.operand is Label l && !labels.Contains(l))throw new Exception("lost branch"); if(c.operand is Label[] ls && ls.Any(l=>!labels.Contains(l)))throw new Exception("lost switch"); }
   total+=count; Console.WriteLine(name+": PASS "+count+" owner-aware substitutions; branch targets retained");
  }
  Console.WriteLine("PASS actual transpiler: "+total+" callsites");
  var audio=plugin.GetType("MainGameVMDPlay.Plugin",true);
  var track=audio.GetMethod("TrackTouchWrites",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(string name in new[]{"OnCollision","HitReactionPlay","ClickAction","DragAction"}) {
   var method=AccessTools.Method(game.GetType("HandCtrl"),name);
   var original=PatchProcessor.GetOriginalInstructions(method);
   int stores=original.Count(c=>c.opcode==OpCodes.Stelem_I4);
   var result=((IEnumerable<CodeInstruction>)track.Invoke(null,new object[]{original})).ToList();
   int writes=result.Count(c=>c.operand is MethodInfo m && m.Name=="StoreTouchRequest");
   if(stores==0 || stores!=writes || result.Any(c=>c.opcode==OpCodes.Stelem_I4)) throw new Exception("touch write contract mismatch: "+name);
   var labels=new HashSet<Label>(result.SelectMany(c=>c.labels));
   foreach(var c in result) { if(c.operand is Label l && !labels.Contains(l))throw new Exception("lost touch branch"); if(c.operand is Label[] ls && ls.Any(l=>!labels.Contains(l)))throw new Exception("lost touch switch"); }
   Console.WriteLine(name+": PASS "+writes+" touch write substitutions; branch targets retained");
  }
 }
}
