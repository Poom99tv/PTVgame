using System;
using System.Collections.Generic;

public sealed class TabsLikeCounter
{
    readonly Dictionary<string,long[]> progress=new Dictionary<string,long[]>();
    public int Add(string viewer,int slot,int likes,int threshold){
        if(string.IsNullOrEmpty(viewer)||slot<0||slot>=7||likes<=0)return 0;
        long[] values;if(!progress.TryGetValue(viewer,out values))progress[viewer]=values=new long[7];
        long sum=values[slot]+likes;threshold=Math.Max(1,threshold);
        int rewards=(int)(sum/threshold);values[slot]=sum%threshold;return rewards;
    }
    public void Clear(){progress.Clear();}
}
