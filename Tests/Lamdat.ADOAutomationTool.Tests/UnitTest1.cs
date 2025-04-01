using System.Globalization;

namespace Lamdat.ADOAutomationTool.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {

        }
        
        [Fact]
        public void tt()
        {
            double r1 = 1.236;
            double r2 = r1 + 111.7;
            var str = r2.ToString("0.00");
            var str2 = r2.ToString();
            // var str3 = (default(double)).ToString("0.00");
            // var str4 = (0d).ToString("0.00");
            var r4 = double.Parse(string.Format("{0:###0.00}", str));
            var r = double.Parse(string.Format("{0:###0.00}", "3.6666"));
            var r5 = double.Parse(string.Format("{0:###0.00}", r2));
            
        }
    }
}