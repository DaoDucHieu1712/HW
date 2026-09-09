namespace HW.PCCP.Solutions;

public class ImportantWordsToAvoidSpoilers
{
    public class Solution
    {
        public int solution(string message, int[,] spoiler_ranges)
        {
            int rangeCount = spoiler_ranges.GetLength(0);

            // coverBy[i] = chỉ số của vùng che đang giấu ký tự thứ i, -1 nghĩa là ký tự đang hiện.
            // Các vùng không giao nhau nên mỗi ký tự thuộc tối đa một vùng.
            int[] coverBy = new int[message.Length];
            Array.Fill(coverBy, -1);

            for (int r = 0; r < rangeCount; r++)
            {
                int start = spoiler_ranges[r, 0];
                int end = spoiler_ranges[r, 1];

                for (int i = start; i <= end; i++)
                {
                    coverBy[i] = r;
                }
            }

            // Một từ bị che chỉ lộ hoàn toàn khi vùng che CUỐI CÙNG của nó được bấm,
            // nên ta gom các từ bị che theo chỉ số vùng đó, giữ nguyên thứ tự trái sang phải.
            // clearWords: các từ nằm hoàn toàn ngoài mọi vùng che (đã lộ sẵn từ đầu).
            var clearWords = new HashSet<string>();
            var revealedAt = new List<string>[rangeCount];

            // Tách từ theo dấu cách, đồng thời tính luôn "bước lộ" của từng từ.
            int wordStart = 0;
            while (wordStart < message.Length)
            {
                if (message[wordStart] == ' ')
                {
                    wordStart++;
                    continue;
                }

                int wordEnd = wordStart;

                // lastRange = vùng che có chỉ số lớn nhất chạm vào từ này.
                // Chỉ cần MỘT ký tự bị che là cả từ bị coi là che, và từ chỉ lộ ở bước lastRange.
                int lastRange = -1;

                while (wordEnd < message.Length && message[wordEnd] != ' ')
                {
                    if (coverBy[wordEnd] > lastRange)
                    {
                        lastRange = coverBy[wordEnd];
                    }

                    wordEnd++;
                }

                string word = message[wordStart..wordEnd];

                if (lastRange < 0)
                {
                    // Từ không dính vùng che nào -> nó "đã xuất hiện ở vùng không che".
                    clearWords.Add(word);
                }
                else
                {
                    // Từ bị che -> xếp vào bước bấm thứ lastRange.
                    (revealedAt[lastRange] ??= []).Add(word);
                }

                wordStart = wordEnd;
            }

            // Bấm lần lượt từng vùng che từ trái sang phải.
            // alreadyRevealed: các từ che đã lộ ở những lần bấm trước (để chống trùng lặp).
            var alreadyRevealed = new HashSet<string>();
            int answer = 0;

            for (int r = 0; r < rangeCount; r++)
            {
                // Vùng này có thể không làm lộ trọn vẹn từ nào (từ còn dính vùng che phía sau).
                if (revealedAt[r] is not { } words)
                {
                    continue;
                }

                // Nhiều từ lộ cùng lúc thì xét lần lượt từ trái sang phải.
                foreach (string word in words)
                {
                    // Ghi nhận từ đã lộ trước, kể cả khi nó không phải từ quan trọng,
                    // vì lần lộ sau của cùng một từ luôn bị coi là trùng lặp.
                    bool firstReveal = alreadyRevealed.Add(word);

                    // Quan trọng <=> lần đầu lộ VÀ chưa từng xuất hiện ở vùng không che.
                    if (firstReveal && !clearWords.Contains(word))
                    {
                        answer++;
                    }
                }
            }

            return answer;
        }
    }
}
