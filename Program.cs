using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iText5PDFBuilder
{
    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            Test();
#else
            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(args[0], Encoding.UTF8));
            Build(config);
#endif
        }

        static void Build(Config config)
        {
            using (Stream output = File.Create(config.outputFile))
            {
                PDFBuilderCore.CreatePdf(output, config);
            }
        }

#if DEBUG
        static void Test()
        {
            Config config = new Config
            {
                title = "Title",
                website = "ketodietapp.com",
                keywords = "keto,diet",
                creator = "itext5 + ketodietapp.com",
                imageUrl = "https://upload.wikimedia.org/wikipedia/commons/thumb/9/97/The_Earth_seen_from_Apollo_17.jpg/1920px-The_Earth_seen_from_Apollo_17.jpg",
                overlayUrl = "https://files.ketodietapp.com/Blog/files/PDFMaker/PDFMakerOverlay-1.png",
                subHeader1 = "header1",
                subHeader2 = "header2",
                body = "This is the body text",
                footer = "footer",
                outputFile = "c:\\somefile.pdf",
            };

            Build(config);
        }
#endif
    }
}
