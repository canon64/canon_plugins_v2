using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[DataContract]
public class PoseClassificationFile
{
    [DataMember(Name = "categories")]
    public Dictionary<string, List<PoseClassificationItem>> Categories;
}

[DataContract]
public class PoseClassificationItem
{
    [DataMember(Name = "nameAnimation")]
    public string NameAnimation;

    [DataMember(Name = "modeInt")]
    public int ModeInt;
}

public class T
{
    public static void Main(string[] args)
    {
        string path = args[0];
        string json = File.ReadAllText(path, Encoding.UTF8);
        var serializer = new DataContractJsonSerializer(typeof(PoseClassificationFile));
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using(var ms = new MemoryStream(bytes))
        {
            var root = serializer.ReadObject(ms) as PoseClassificationFile;
            Console.WriteLine(root == null ? "root=null" : "root!=null");
            Console.WriteLine(root?.Categories == null ? "categories=null" : "categories=" + root.Categories.Count);
            if(root?.Categories != null)
            {
                int entries = 0;
                foreach(var kv in root.Categories)
                {
                    entries += kv.Value == null ? 0 : kv.Value.Count;
                }
                Console.WriteLine("entries=" + entries);
            }
        }
    }
}
