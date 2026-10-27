using System.Collections.Generic;
namespace MainGameVMDPlay {
 internal static class ClothingTargets {
  // Take a copy at key acceptance; later UI selection or roster changes cannot retarget it.
  internal static List<T> Capture<T>(int selection, IEnumerable<T> main, T independent) where T:class {
   var result=new List<T>();
   if(selection!=0 && main!=null) foreach(var target in main) if(target!=null && !ReferenceEquals(target,independent) && !result.Contains(target)) result.Add(target);
   if(selection!=1 && independent!=null && !result.Contains(independent)) result.Add(independent);
   return result;
  }
 }
}
