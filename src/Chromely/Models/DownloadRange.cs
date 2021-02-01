namespace Chromely.Models
{
    internal class DownloadRange
    {
        #region Properties

        public long Start { get; set; }

        public long End { get; set; }

        #endregion

        #region Constructor

        public DownloadRange(long start, long end)
        {
            Start = start;
            End = end;
        }

        #endregion
    }
}