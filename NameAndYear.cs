using System;
using System.Collections.Generic;
using System.Text;

namespace MediaStreamer.IO
{
    public class NameAndYear
    {
        public string Name { get; set; }
        public string Year { get; set; }

        public string GetName()
        {
            return Name;
        }

        public long? GetYear()
        {
            if (string.IsNullOrEmpty(Year))
                return null;

            long result = 0;
            if (long.TryParse(Year, out result))
                return result;
            return 0L;
        }
    }
}
