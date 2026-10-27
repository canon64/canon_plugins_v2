namespace MainGameVMDPlay {
 // Identity and one-shot commit shared by the Unity lifecycle and regression harness.
 internal class PlaybackSuspension {
  internal long Token;
  internal int SceneGeneration,TargetGeneration;
  internal bool WasPlaying;
  internal float Time;
  internal string[] Queue;
  internal int Index;
  internal bool ListRunning;
  internal void CapturePlaylist(string[] queue,int index,bool running) {
   Queue=queue==null?null:(string[])queue.Clone(); Index=index; ListRunning=running;
  }
  internal bool Restored { get; private set; }
  private bool _consumed;
  internal bool Matches(long token,int scene,int target) => !_consumed && Token==token && SceneGeneration==scene && TargetGeneration==target;
  internal bool MarkRestored(long token,int scene,int target) {
   if(!Matches(token,scene,target)) return false;
   Restored=true; return true;
  }
  internal bool Commit(long token,int scene,int target) {
   if(!Restored || !Matches(token,scene,target)) return false;
   _consumed=true; return true;
  }
  internal void Discard() { _consumed=true; Restored=false; }
 }
}
