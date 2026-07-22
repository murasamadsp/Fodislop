namespace Fodinae.Scripts.UI.Programmator
{
    public static class ProgrammatorData
    {
        public const int COLS = 16;
        public const int ROWS = 12;
        public const int PAGES = 16;
        public const int CELLS_PER_PAGE = COLS * ROWS;
        public const int TOTAL_CELLS = PAGES * CELLS_PER_PAGE;

        public static int[] Codes = new int[TOTAL_CELLS];
        public static int[] Nums = new int[TOTAL_CELLS];
        public static string[] Labels = new string[TOTAL_CELLS];

        public static int CurrentPage;
        public static int HoveredCell = -1;

        public static readonly int[] WOPERATORS = { 29, 31, 32, 33, 35, 131 };
        public static readonly int[] SHIFTWOPERATORS = { 29, 31, 33, 36, 37, 132 };
    }
}
