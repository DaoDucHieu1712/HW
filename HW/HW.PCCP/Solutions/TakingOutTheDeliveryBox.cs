namespace HW.PCCP.Solutions;

public class TakingOutTheDeliveryBox
{
    public class Solution
    {
        public int solution(int n, int w, int num)
        {
            // Đổi số hiệu thùng -> (tầng, cột). Đếm từ 1 nên phải trừ 1 trước rồi cộng lại.
            int floor = (num - 1) / w + 1;
            int offset = (num - 1) % w;

            // offset là THỨ TỰ ĐẶT trong tầng, không phải cột: tầng chẵn xếp phải->trái
            // nên thùng thứ offset nằm ở cột w - offset.
            int col = IsLeftToRight(floor) ? offset + 1 : w - offset;

            int topFloor = (n + w - 1) / w;
            int count = 0;

            // Bắt đầu từ chính floor vì đáp án tính cả num.
            for (int t = floor; t <= topFloor; t++)
            {
                // Tính lại từ đầu mỗi tầng: bước nhảy giữa hai tầng liên tiếp không đều.
                int box = BoxAt(t, col, w);

                // Chỗ DUY NHẤT xử lý tầng trên cùng bị khuyết. Vì kiểm tra bằng số hiệu
                // thùng nên tự động đúng cả khi tầng khuyết là tầng chẵn (trống phía TRÁI).
                if (box <= n)
                {
                    count++;
                }
            }

            return count;
        }

        // Tầng lẻ xếp trái->phải, tầng chẵn xếp phải->trái.
        private static bool IsLeftToRight(int floor) => floor % 2 == 1;

        // Số hiệu thùng ở (tầng, cột) nếu tầng đó được lấp đầy.
        private static int BoxAt(int floor, int col, int w) =>
            (floor - 1) * w + (IsLeftToRight(floor) ? col : w + 1 - col);
    }
}
