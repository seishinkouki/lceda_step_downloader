using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace lceda_step_downloader.Models.Component
{
    public class Modifier
    {

        public string uuid { get; set; }

        public string username { get; set; }

        public string nickname { get; set; }

        public string avatar { get; set; }
    }

    public class Creator
    {

        public string uuid { get; set; }

        public string username { get; set; }

        public string nickname { get; set; }

        public string avatar { get; set; }
    }


    public class Owner
    {

        public string uuid { get; set; }

        public string username { get; set; }

        public string nickname { get; set; }

        public string avatar { get; set; }
    }

    public class Tags
    {

        public List<string> parent_tag { get; set; }

        public List<string> child_tag { get; set; }
    }

    public class Result
    {

        public string uuid { get; set; }

        public Modifier modifier { get; set; }

        public Creator creator { get; set; }

        public Owner owner { get; set; }

        public string description { get; set; }

        public int docType { get; set; }

        public string dataStr { get; set; }

        public Tags tags { get; set; }

        [JsonPropertyName("public")]
        public bool _public { get; set; }

        public string source { get; set; }

        [JsonIgnore]
        public string version { get; set; }

        public int type { get; set; }

        public string title { get; set; }

        public int createTime { get; set; }

        public int updateTime { get; set; }

        public string created_at { get; set; }

        public string display_title { get; set; }

        public string updated_at { get; set; }

        public int ticket { get; set; }

        public string std_uuid { get; set; }

        [JsonPropertyName("3d_model_uuid")]
        public string _3d_model_uuid { get; set; }

        public bool has_device { get; set; }
}

public class Component
{

    public bool success { get; set; }

    public int code { get; set; }

    public Result result { get; set; }
}
}
