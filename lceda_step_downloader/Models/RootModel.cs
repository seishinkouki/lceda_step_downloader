using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace lceda_step_downloader.Models.Root
{
    public class SearchSite
    {
        public string Site { get; set; }
        public int Value { get; set; }
    }
    public class Owner
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

    public class Modifier
    {
        public string uuid { get; set; }
        public string username { get; set; }
        public string nickname { get; set; }
        public string avatar { get; set; }
    }

    public class Parent_tag
    {
        public string uuid { get; set; }
        public string name { get; set; }
        public string name_cn { get; set; }
    }

    public class Child_tag
    {
        public string uuid { get; set; }
        public string name { get; set; }
        public string name_cn { get; set; }
    }

    public class Tags
    {
        public Parent_tag parent_tag { get; set; }
        public Child_tag child_tag { get; set; }
    }

    public class Attributes
    {
        [JsonPropertyName("LCSC Part Name")]
        public string LCSC_Part_Name { get; set; }

        [JsonPropertyName("Supplier Part")]
        public string Supplier_Part { get; set; }

        public string Manufacturer { get; set; }

        [JsonPropertyName("Manufacturer Part")]
        public string Manufacturer_Part { get; set; }

        [JsonPropertyName("Supplier Footprint")]
        public string Supplier_Footprint { get; set; }

        [JsonPropertyName("JLCPCB Part Class")]
        public string JLCPCB_Part_Class { get; set; }

        public string Datasheet { get; set; }

        public string Supplier { get; set; }

        [JsonPropertyName("Add into BOM")]
        public string Add_into_BOM { get; set; }

        [JsonPropertyName("Convert to PCB")]
        public string Convert_to_PCB { get; set; }

        public string Symbol { get; set; }

        public string Footprint { get; set; }

        [JsonPropertyName("3D Model")]
        public string _3D_Model { get; set; }

        [JsonPropertyName("3D Model Title")]
        public string _3D_Model_Title { get; set; }

        [JsonPropertyName("3D Model Transform")]
        public string _3D_Model_Transform { get; set; }

        [JsonPropertyName("Connector Type")]
        public string Connector_Type { get; set; }

        public string Standard { get; set; }

        public string Gender { get; set; }

        [JsonPropertyName("Number of Contacts")]
        public string Number_of_Contacts { get; set; }

        [JsonPropertyName("Number of Ports")]
        public string Number_of_Ports { get; set; }

        [JsonPropertyName("Mounting Style")]
        public string Mounting_Style { get; set; }

        [JsonPropertyName("Current Rating - Power (Max)")]

        public string Current_Rating_Power { get; set; }

        [JsonPropertyName("Operating Temperature Range")]
        public string Operating_Temperature_Range { get; set; }

        [JsonPropertyName("Welding Temperature(Max)")]
        public string Welding_Temperature { get; set; }
    }

    public class Symbol
    {

        public string uuid { get; set; }

        public string title { get; set; }

        public string display_title { get; set; }
    }

    public class Footprint
    {

        public string uuid { get; set; }

        public string title { get; set; }

        public string display_title { get; set; }
    }

    public class ResultItem
    {

        public string uuid { get; set; }

        public Owner owner { get; set; }

        public Creator creator { get; set; }

        public Modifier modifier { get; set; }

        public string description { get; set; }

        public string title { get; set; }

        //public Tags tags { get; set; }

        public List<string> images { get; set; }

        public Attributes attributes { get; set; }

        public string source { get; set; }
        [JsonIgnore]
        public int version { get; set; }

        public string project_uuid { get; set; }

        public int footprint_type { get; set; }

        public int symbol_type { get; set; }

        public string product_code { get; set; }

        public int updateTime { get; set; }

        public int createTime { get; set; }

        public string display_title { get; set; }

        public string created_at { get; set; }

        public string updated_at { get; set; }

        public int ticket { get; set; }

        public Symbol symbol { get; set; }

        public Footprint footprint { get; set; }
    }

    public class Root
    {

        public bool success { get; set; }

        public int code { get; set; }

        public List<ResultItem> result { get; set; }
    }

}
