using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Downloader
{
    public class AsyncDownloadCompletedEventArgs : System.ComponentModel.AsyncCompletedEventArgs
    {
        public AsyncDownloadCompletedEventArgs(int taskId, Exception error, bool cancelled, object userState) : base(error, cancelled, userState)
        {
            TaskId = taskId;
        }

        public int TaskId { get; }
    }
}
