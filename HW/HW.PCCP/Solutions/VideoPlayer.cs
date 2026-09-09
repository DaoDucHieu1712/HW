namespace HW.PCCP.Solutions;

public class VideoPlayer
{
    public class Solution
    {
        public string solution(string video_len, string pos, string op_start, string op_end, string[] commands)
        {
            string answer = "";
            var video_len_timeSpan = TimeSpan.ParseExact(video_len, @"mm\:ss", null);
            var pos_timeSpan = TimeSpan.ParseExact(pos, @"mm\:ss", null);
            var op_start_timeSpan = TimeSpan.ParseExact(op_start, @"mm\:ss", null);
            var op_end_timeSpan = TimeSpan.ParseExact(op_end, @"mm\:ss", null);

            if (pos_timeSpan >= op_start_timeSpan && pos_timeSpan <= op_end_timeSpan)
            {
                pos_timeSpan = op_end_timeSpan;
            }

            foreach (var command in commands)
            {
                if (command == "prev")
                {
                    pos_timeSpan = pos_timeSpan.Subtract(TimeSpan.FromSeconds(10));
                    if (pos_timeSpan < TimeSpan.Zero)
                    {
                        pos_timeSpan = TimeSpan.Zero;
                    }
                }
                else if (command == "next")
                {
                    pos_timeSpan = pos_timeSpan.Add(TimeSpan.FromSeconds(10));
                    if (pos_timeSpan > video_len_timeSpan)
                    {
                        pos_timeSpan = video_len_timeSpan;
                    }
                }

                if (pos_timeSpan >= op_start_timeSpan && pos_timeSpan <= op_end_timeSpan)
                {
                    pos_timeSpan = op_end_timeSpan;
                }
            }

            answer = pos_timeSpan.ToString(@"mm\:ss");
            return answer;
        }
    }
}
