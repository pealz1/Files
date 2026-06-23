// Copyright (c) Files Community. Licensed under the MIT License.

using Files.App.Utils.SaveDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.SaveDialog.UnitTests
{
	[TestClass]
	public class SaveDialogPathHelperTests
	{
		[TestMethod]
		public void ParseFileTypes_ParsesDisplayAndPatternPairs()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("PNG Image|*.png|All files|*.*");

			Assert.AreEqual(2, result.Count);
			Assert.AreEqual("PNG Image", result[0].Display);
			Assert.AreEqual("*.png", result[0].Pattern);
			Assert.AreEqual(".png", result[0].PrimaryExtension);
			Assert.AreEqual("All files", result[1].Display);
			Assert.AreEqual("", result[1].PrimaryExtension);
		}

		[TestMethod]
		public void ParseFileTypes_EmptyOrNull_ReturnsAllFilesFallback()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("");

			Assert.AreEqual(1, result.Count);
			Assert.AreEqual("*.*", result[0].Pattern);
		}

		[TestMethod]
		public void ParseFileTypes_MultiExtensionPattern_UsesFirstAsPrimary()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("JPEG|*.jpg;*.jpeg");

			Assert.AreEqual(".jpg", result[0].PrimaryExtension);
		}

		[TestMethod]
		public void ResolveTargetPath_AppendsExtensionWhenMissing()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "photo", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\photo.png", path);
		}

		[TestMethod]
		public void ResolveTargetPath_RespectsTypedExtension()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "photo.jpg", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\photo.jpg", path);
		}

		[TestMethod]
		public void ResolveTargetPath_AllFilesType_DoesNotAppend()
		{
			var type = new FileTypeChoice("All files", "*.*", "");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "notes", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\notes", path);
		}

		[TestMethod]
		public void ResolveTargetPath_TypedAbsolutePath_OverridesWorkingDirectory()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", @"D:\stuff\a", type);

			Assert.AreEqual(@"D:\stuff\a.png", path);
		}

		[TestMethod]
		public void IsValidFileName_RejectsInvalidChars()
		{
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("bad:name?.png"));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("   "));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName(""));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("CON"));
		}

		[TestMethod]
		public void IsValidFileName_AcceptsNormalNames()
		{
			Assert.IsTrue(SaveDialogPathHelper.IsValidFileName("5Julygamer.png"));
			Assert.IsTrue(SaveDialogPathHelper.IsValidFileName(@"D:\stuff\a.png"));
		}
	}
}
