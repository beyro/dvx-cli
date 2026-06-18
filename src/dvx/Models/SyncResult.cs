namespace dvx.Models
{
    public class SyncResult
    {
        public int           Created   { get; set; }
        public int           Updated   { get; set; }
        public int           Deleted   { get; set; }
        public int           Skipped   { get; set; }   // unchanged (web resource sync)
        public int           Published { get; set; }   // count published (web resource sync)
        public List<string>  Warnings  { get; set; } = new();
        public List<string>  Errors    { get; set; } = new();
    }
}
