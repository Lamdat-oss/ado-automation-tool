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
        public void CheckDoubleValueInMemory()
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

        [Fact]
        public void ParseFileName_FileNameWithPathAndSlashes_FileNameFetchedSuccessfully()
        {
            // Arrange
            string fileName = String.Empty;
            
            // Act
            var filePath = "scripts/level/someFile.jpeg\\".Split("scripts/level", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (filePath.Length > 0 && filePath.Last().Length > 0)
            {
                fileName = filePath.Last();
                if (fileName[0] == '/' || fileName[0] == '\\')
                {
                    fileName = fileName.Substring(1, fileName.Length - 1);
                }

                var lastIndex = fileName.Length - 1;
                if (lastIndex > 0 && (fileName[lastIndex] == '/' || fileName[lastIndex] == '\\'))
                {
                    fileName = fileName.Substring(0, fileName.Length - 1);
                }
            }
            
            // Assert
            Assert.Equal("someFile.jpeg", fileName);
        }
    }
}