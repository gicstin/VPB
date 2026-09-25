using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SlotPickerLabelTests
    {
        public SlotPickerLabelTests(VamFixture vam) { }

        [Theory]
        [InlineData("Author.Pkg.1:/Custom/Clothing/Female/Top/Shirt/Shirt.vam", "clothing", "Top: Shirt")]
        [InlineData("Author.Pkg.1:\\Custom\\Clothing\\Female\\Top\\Shirt\\Shirt.vam", "clothing", "Top: Shirt")]
        [InlineData("Author.Pkg.1:/Custom/Hair/Female/LONG/Braid/Braid.vam", "hair", "Long: Braid")]
        [InlineData("D:/VaM/Custom/Clothing/Female/Shoes/Heels/Heels.vam", "clothing", "Shoes: Heels")]
        [InlineData("Author.Pkg.1:/Custom/Clothing/Female", "clothing", "Female")]
        public void LabelIsTypeFolderAndFileName(string path, string folder, string expected)
        {
            Assert.True(GalleryPanel.SlotPickerLabelFromPath(path, folder, "fallback") == expected,
                "The slot picker row for " + path + " would read '" + GalleryPanel.SlotPickerLabelFromPath(path, folder, "fallback") +
                "' instead of '" + expected + "', so the user cannot tell which worn item they are replacing.");
        }

        [Theory]
        [InlineData(null, "clothing")]
        [InlineData("", "hair")]
        [InlineData("Author.Pkg.1:/Custom/Clothing/Female/Top/Shirt/Shirt.vam", "hair")]
        public void PathOutsideTheCategoryIsNotOffered(string path, string folder)
        {
            Assert.True(GalleryPanel.SlotPickerLabelFromPath(path, folder, "fallback") == null,
                "A worn item outside the " + folder + " folder must not appear in the " + folder + " slot picker.");
        }
    }
}
