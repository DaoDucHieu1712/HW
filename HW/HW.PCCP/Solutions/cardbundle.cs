namespace HW.PCCP.Solutions;

public class CardBundle
{
    public class Solution
    {
        public string solution(string[] cards1, string[] cards2, string[] goal)
        {
            // Mỗi xấp bài là một hàng đợi: chỉ Peek/Dequeue được lá trên cùng,
            // không được bỏ qua lá nào, không được đổi thứ tự.
            var deck1 = new Queue<string>(cards1);
            var deck2 = new Queue<string>(cards2);

            foreach (string want in goal)
            {
                // Mọi từ trên cả hai xấp đều khác nhau nên hai đỉnh không bao giờ trùng
                // -> tối đa MỘT xấp khớp được từ đang cần. Không phân nhánh, không cần quay lui.
                if (deck1.Count > 0 && deck1.Peek() == want)
                {
                    deck1.Dequeue();
                }
                else if (deck2.Count > 0 && deck2.Peek() == want)
                {
                    deck2.Dequeue();
                }
                else
                {
                    // Không xấp nào đưa ra được từ đang cần -> bế tắc, và vì chỉ có một
                    // đường đi duy nhất nên chắc chắn không còn cách nào khác.
                    return "No";
                }
            }

            // Điều kiện thắng là ghép hết goal, không cần dùng hết bài.
            return "Yes";
        }
    }
}
