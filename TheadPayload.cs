using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ThreadingExamples
{
    class ThreadPayload
    {
        public ThreadPayload()
        {
            PayloadId = GetNextID();
        }

        public int Delay { get; set; }
        public int NumLoops { get; set; }
        public int PayloadId { get; set; }
        public string Message { get; set; }

        public int Count { get; set; }


        private static int _id;
        private static int GetNextID()
        {
            return _id++;
        } 
    }
}
