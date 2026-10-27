using System;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using MainGameVMDPlay;
class Program {
 static int count;
 static void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
 static VMDPlaySettings Read(string json) { using(var s=new MemoryStream(Encoding.UTF8.GetBytes(json))) return (VMDPlaySettings)new DataContractJsonSerializer(typeof(VMDPlaySettings)).ReadObject(s); }
 static void Main() { try { Run(); } catch(Exception e) { Console.WriteLine(e.GetType().FullName); Console.WriteLine(e.Message); Environment.ExitCode=1; } }
 static void Run() {
  var cycle=new MainGameHScenePoseInjector.AutoCycleSettings(); cycle.HohoAka.Enabled=true; cycle.HohoAka.HoldSeconds=7; cycle.ModSweat.ManualValue=2;
  var savedCycle=cycle.SuspensionCopy(); cycle.HohoAka.Enabled=false; cycle.HohoAka.HoldSeconds=1; cycle.ModSweat.ManualValue=0;
  Check(savedCycle.HohoAka.Enabled && savedCycle.HohoAka.HoldSeconds==7 && savedCycle.ModSweat.ManualValue==2,"Astral effect settings are deep isolated from dream edits");

  object h1=new object(),h2=new object(),actor=new object();
  var hRoster=new System.Collections.Generic.List<object>{h1,h2};
  var independent=ClothingTargets.Capture(0,hRoster,actor);
  Check(independent.Count==1 && ReferenceEquals(independent[0],actor),"independent radio excludes both H women");
  Check(ClothingTargets.Capture(0,hRoster,(object)null).Count==0,"missing independent never falls back to H");
  var main=ClothingTargets.Capture(1,hRoster,actor);
  Check(main.Count==2 && main.Contains(h1) && main.Contains(h2) && !main.Contains(actor),"main radio includes all H women only");
  var both=ClothingTargets.Capture(2,hRoster,actor); hRoster.Clear();
  Check(both.Count==3 && both.Contains(actor),"both radio snapshots roster at acceptance");
  Check(main.Count==2,"later roster changes cannot retarget accepted clothing operation");

  var liveSettings = new VMDPlaySettings {LastVmdAbsPath="dance.vmd",LastAudioAbsPath="music.mp4",CameraVmdPath="camera.vmd",AudioVolume=.37f,MyListLoop=true};
  liveSettings.Adjustments["char.footIK"]=1; liveSettings.Adjustments["body.0"]=1;
  liveSettings.MyLists["list"]=new System.Collections.Generic.List<string>{"A","A","B"};
  var frozenSettings=liveSettings.SuspensionCopy();
  liveSettings.Adjustments["char.footIK"]=0; liveSettings.MyLists["list"].RemoveAt(0); liveSettings.AudioVolume=0;
  Check(frozenSettings.Adjustments["char.footIK"]==1 && frozenSettings.Adjustments["body.0"]==1,"suspension retains all adjustments independently of presets");
  Check(frozenSettings.MyLists["list"].Count==3 && frozenSettings.AudioVolume==.37f && frozenSettings.MyListLoop,"suspension isolates settings and repeated tracks");
  Check(frozenSettings.LastAudioAbsPath=="music.mp4" && frozenSettings.LastVmdAbsPath=="dance.vmd" && frozenSettings.CameraVmdPath=="camera.vmd","suspension retains all media paths");
  foreach(bool playing in new[]{false,true}) {
   var frozen = new PlaybackSuspension {Token=7,SceneGeneration=3,TargetGeneration=9,Time=82.125f,WasPlaying=playing};
   var queue=new[]{"A","A","B"}; frozen.CapturePlaylist(queue,1,true); queue[1]="C";
   Check(frozen.Queue[1]=="A" && frozen.Index==1 && frozen.ListRunning,"duplicate playlist entry retained by position");
   Check(!frozen.Commit(7,3,9),"cannot resume before restore finishes");
   Check(!frozen.MarkRestored(6,3,9) && !frozen.MarkRestored(7,4,9) && !frozen.MarkRestored(7,3,10),"old token scene and replaced actor cannot restore");
   Check(frozen.MarkRestored(7,3,9) && frozen.Commit(7,3,9),"valid restore commits once");
   Check(!frozen.Commit(7,3,9),"duplicate return cannot restart music");
   Check(frozen.Time==82.125f && frozen.WasPlaying==playing,"stay duration does not change cursor or paused intent");
  }
  var cancelled=new PlaybackSuspension{Token=8,SceneGeneration=1,TargetGeneration=1}; cancelled.Discard();
  Check(!cancelled.MarkRestored(8,1,1) && !cancelled.Commit(8,1,1),"delete and scene exit invalidate delayed restoration");

  var touch = new TouchVoiceRequests();
  touch.Record(0, false, 103, 10);
  Check(touch.Matches(0, false, 103, 10), "touch speech admitted immediately");
  Check(!touch.Matches(1, false, 103, 10) && !touch.Matches(0, true, 103, 10), "touch scope separates actors and short voices");
  Check(!touch.Matches(0, false, 104, 10) && !touch.Matches(0, false, 103, 13), "unrelated and stale automatic requests rejected");
  touch.Clear(0, false); Check(!touch.Matches(0, false, 103, 11), "consumed request cannot replay");
  touch.Record(0, false, 103, 12); Check(touch.Matches(0, false, 103, 12), "same ID from a new touch admitted");
  touch.Reset(); Check(!touch.Matches(0, false, 103, 12), "release invalidates touch requests");
  var destroyed = new System.Collections.Generic.List<string>();
  var firstLoad = new OwnedLoadGeneration<string>(destroyed.Add);
  var secondLoad = new OwnedLoadGeneration<string>(destroyed.Add);
  int applied = 0;
  firstLoad.Cancel(); firstLoad.Accept("late", true, _ => applied++);
  Check(applied == 0 && destroyed.Contains("late"), "cancel before callback recovers object");
  secondLoad.Accept("new", true, _ => applied++);
  Check(applied == 1 && !destroyed.Contains("new"), "old callback does not cancel new generation");
  secondLoad.Cancel(); secondLoad.Cancel();
  Check(destroyed.FindAll(x => x == "new").Count == 1, "cancel after callback and double disposal");
  var failed = new OwnedLoadGeneration<string>(destroyed.Add);
  failed.Accept("failed", true, _ => { throw new Exception("callback"); });
  Check(failed.Error != null && destroyed.Contains("failed"), "callback failure captured and object recovered");
  failed.Cancel(); Check(destroyed.FindAll(x => x == "failed").Count == 1, "callback failure is not destroyed twice");
  var orphan = new OwnedLoadGeneration<string>(destroyed.Add);
  orphan.Accept("dead-owner", false, _ => applied++);
  Check(applied == 1 && destroyed.Contains("dead-owner"), "dead owner rejects completion");
  orphan.Accept("unparented", true, _ => {}); orphan.Accept("child", true, _ => {});
  orphan.Reconcile(x => x == "child");
  Check(destroyed.Contains("unparented") && !destroyed.Contains("child"), "ready recovers unparented objects only");
  Check(new VMDPlaySettings { LastActorCardDirectory="private-card-dir" }.PresetCopy().LastActorCardDirectory == "", "card location not stored in dance preset");
  var invalidated = new OwnedLoadGeneration<string>(destroyed.Add);
  invalidated.Accept("owned-before-invalidate", true, _ => {});
  invalidated.Invalidate(); invalidated.Accept("late-before-cleanup", true, _ => applied++);
  invalidated.Cancel();
  Check(destroyed.Contains("owned-before-invalidate") && destroyed.Contains("late-before-cleanup") && applied==1, "invalidation rejects callbacks before cleanup completes");
  var transition = new ControlTransition();
  int loading = transition.Generation, speech = transition.Generation;
  Check(transition.TryBeginRelease(), "posture change begins release");
  Check(transition.Generation != loading && transition.Generation != speech, "posture change invalidates load and speech");
  Check(!transition.TryBeginRelease(), "nested posture hook cannot release twice");
  var order = new System.Collections.Generic.List<string>();
  ControlTransition.Cleanup(e => order.Add("error"), () => order.Add("stop"), () => { throw new Exception("media failure"); }, () => order.Add("restore"), () => transition.CompleteRelease());
  Check(string.Join(",", order)=="stop,error,restore" && !transition.Releasing, "failure still restores control in order");
  ControlTransition.Cleanup(e => { throw new Exception("logger failure"); }, () => { throw new Exception(); }, () => order.Add("unlocked"));
  Check(order[order.Count-1]=="unlocked", "logging failure cannot retain control");
  int next = transition.Generation; transition.Invalidate();
  Check(next != transition.Generation && transition.TryBeginRelease(), "new scene invalidates callbacks and permits next release");
  transition.CompleteRelease();
  Check(Read("{}").AudioVolume==1, "legacy preset defaults to 100%");
  Check(Read("{\"AudioVolume\":0}").AudioVolume==0, "mute retained");
  var settings = new VMDPlaySettings { AudioVolume=0.35f, LastVmdAbsPath="dance.vmd" };
  using(var s=new MemoryStream()) { var serializer=new DataContractJsonSerializer(typeof(VMDPlaySettings)); serializer.WriteObject(s,settings);s.Position=0;var copy=(VMDPlaySettings)serializer.ReadObject(s); Check(copy.AudioVolume==0.35f && copy.LastVmdAbsPath=="dance.vmd", "volume roundtrip"); }
  var names=new[]{"曲", "長い曲"};
  var hits=DanceTriggerMatcher.Find("長い曲を踊るね。曲を踊るね",names);
  Check(hits.Count==2 && hits[0].Name=="長い曲" && hits[0].Index==0 && hits[1].Name=="曲", "longest match per occurrence");
  Check(DanceTriggerMatcher.Find("未知を踊るね",names)[0].Name==null, "unknown does not play");
  Check(DanceTriggerMatcher.Find("曲を踊って",names).Count==0, "imperative goes through SubtitleCore conversion");
  Check(DanceTriggerMatcher.Find("曲を踊るかも",names).Count==0, "no unrelated trigger");
  Check(DanceTriggerMatcher.Find("曲を踊ります", names, "を踊ります").Count==1, "custom suffix");
  Check(DanceTriggerMatcher.Find("曲を踊るね", names, "").Count==0, "empty suffix disables");
  Check(Read("{}").MyLists != null && Read("{}").MyLists.Count==0 && !Read("{}").MyListLoop, "legacy mylist defaults");
  var listSettings = new VMDPlaySettings { MyListLoop=true, SelectedMyList="お気に入り" };
  listSettings.MyLists["お気に入り"]=new System.Collections.Generic.List<string>{"曲A","曲B","曲A"};
  using(var stream=new MemoryStream()) { var serializer=new DataContractJsonSerializer(typeof(VMDPlaySettings));serializer.WriteObject(stream,listSettings);stream.Position=0;var copy=(VMDPlaySettings)serializer.ReadObject(stream);Check(copy.MyListLoop && copy.SelectedMyList=="お気に入り" && string.Join(",",copy.MyLists["お気に入り"])=="曲A,曲B,曲A", "list order duplicates and loop survive restart"); }
  var model = new VMDPlaySettings();
  model.Adjustments["char.enableIK"] = 0;
  model.Adjustments["char.IKWeight.disableToeIK"] = 1;
  model.Adjustments["char.quickAdjust.ArmUp"] = 12.5f;
  model.Adjustments["body.0"] = 0;
  var savedModel = model.PresetCopy();
  Check(!savedModel.Adjustments.ContainsKey("body.0") && model.Adjustments.ContainsKey("body.0"), "exclude body without mutating current settings");
  using(var stream=new MemoryStream()) { var serializer=new DataContractJsonSerializer(typeof(VMDPlaySettings));serializer.WriteObject(stream,savedModel);stream.Position=0;var copy=(VMDPlaySettings)serializer.ReadObject(stream);Check(copy.Adjustments["char.enableIK"]==0 && copy.Adjustments["char.IKWeight.disableToeIK"]==1 && copy.Adjustments["char.quickAdjust.ArmUp"]==12.5f, "foot toe and model settings roundtrip"); }
  Check(MediaTimeline.Position(10, 120, false)==10 && MediaTimeline.Position(17.5f, 120, false)==17.5f, "stall skips directly to player cursor");
  Check(MediaTimeline.Position(4, 120, false)==4, "backward seek has no accumulated offset");
  Check(MediaTimeline.Position(120, 120, false)==120 && MediaTimeline.Position(145, 120, false)==120, "end clamps");
  Check(MediaTimeline.Position(145, 120, true)==25 && MediaTimeline.Position(120, 120, true)==0, "loop maps absolute cursor");
  Check(MediaTimeline.Position(-1, 120, true)==0 && MediaTimeline.Position(1, 0, true)==0, "empty and negative cursor");
  var seek = new LatestSeek(); float target;
  seek.Request(10); Check(seek.TryBegin(out target) && target==10, "click dispatch without release");
  seek.Request(20); seek.Request(5); Check(!seek.TryBegin(out target) && seek.Target==5, "in flight keeps latest reverse drag");
  seek.Complete(); Check(seek.TryBegin(out target) && target==5, "completion dispatches latest only");
  seek.Complete(); Check(!seek.TryBegin(out target), "no duplicate after completion");
  seek.Request(30); seek.Request(40); Check(seek.TryBegin(out target) && target==40, "frame input coalesces");
  seek.Request(60); seek.Reset(); Check(!seek.Pending && !seek.TryBegin(out target), "release discards old requests");
  var randomHits = DanceTriggerMatcher.Find("ランダムに踊るね", names);
  Check(randomHits.Count==1 && randomHits[0].IsRandom && randomHits[0].Index==0, "random command schedules once");
  Check(DanceTriggerMatcher.Find("ランダムに踊るね", names, "を踊ります")[0].IsRandom, "random independent of named suffix");
  Check(DanceTriggerMatcher.Find("ランダムに踊れ", names).Count==0, "random imperative converted upstream");
  Check(PlaylistNavigation.Step(0, 3, -1, false)==-1 && PlaylistNavigation.Step(2,3,1,false)==-1, "playlist edges disabled");
  Check(PlaylistNavigation.Step(0, 3, -1, true)==2 && PlaylistNavigation.Step(2,3,1,true)==0, "playlist wraps both ways");
  Check(PlaylistNavigation.Step(0, 3, 1, false)==1 && PlaylistNavigation.Step(2,3,-1,false)==1, "duplicate names navigate by position");
  Check(PlaylistNavigation.Step(-1, 0, 1, true)==-1 && PlaylistNavigation.Step(-1,3,1,false)==0, "empty and unselected list");
  Check(PlaylistNavigation.Step(0,1,1,false)==-1 && PlaylistNavigation.Step(0,1,1,true)==0, "single entry loop");
  foreach(float width in new float[]{480, 720, 1080, 1920}) foreach(bool expanded in new[]{false,true}) {
   float top = expanded ? 540 : 32;
   float height = MainGameBlankMapAdd.PlaybackTransportLayout.MinimumHeight(width, expanded);
   float list = top + MainGameBlankMapAdd.PlaybackTransportLayout.ListOffset(width);
   float audioTop = height - 20 - (MainGameBlankMapAdd.PlaybackTransportLayout.SeparateAudio(width) ? 26 : 0);
   Check(list+22 <= list+26 && list+26+38 <= audioTop, "transport rows separated " + width + " expanded=" + expanded);
   float choices = MainGameBlankMapAdd.PlaybackTransportLayout.ChoiceWidth(width);
   Check(6+choices+6+choices<=width-6 && choices>=200, "choices on screen " + width);
   float seekStart=118;
   float seekEnd=MainGameBlankMapAdd.PlaybackTransportLayout.SeparateAudio(width) ? width-6 : width-340;
   float seekWidth=MainGameBlankMapAdd.PlaybackTransportLayout.SeekWidth(seekStart,seekEnd,true);
   Check(seekWidth>=40 && seekStart+seekWidth+68<=seekEnd, "single Handy beside seek " + width);
  }
  Console.WriteLine("PASS "+count+" preset compatibility and trigger checks");
 }
}
