// Copyright (c) Files Community. Licensed under the MIT License.

using System.Linq;
using Files.App.Data.Enums;
using Files.App.Utils.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.SaveDialog.UnitTests
{
	[TestClass]
	public class CommandLineSaveParsingTests
	{
		[TestMethod]
		public void Parses_SaveDialog_Args()
		{
			var cmd = "-directory \"C:\\Users\\me\\Downloads\" -outputpath \"C:\\Temp\\out.tmp\" " +
					  "-savedialog -saveas \"photo.png\" -filetypes \"PNG|*.png|All files|*.*\" -filetypeindex 1";

			var parsed = CommandLineParser.ParseUntrustedCommands(cmd);

			Assert.IsTrue(parsed.Any(c => c.Type == ParsedCommandType.SaveDialog));
			Assert.AreEqual("photo.png", parsed.First(c => c.Type == ParsedCommandType.SaveAs).Payload);
			Assert.AreEqual("PNG|*.png|All files|*.*", parsed.First(c => c.Type == ParsedCommandType.FileTypes).Payload);
			Assert.AreEqual("1", parsed.First(c => c.Type == ParsedCommandType.FileTypeIndex).Payload);
			Assert.AreEqual(ParsedCommandType.OutputPath, parsed.First(c => c.Type == ParsedCommandType.OutputPath).Type);
		}

		[TestMethod]
		public void Identifies_Save_And_Open_Dialog_Activations()
		{
			Assert.IsTrue(CommandLineParser.IsFileDialogActivation(
				"-directory \"C:\\Users\\me\\Downloads\" -outputpath \"C:\\Temp\\save.tmp\" -savedialog"));
			Assert.IsTrue(CommandLineParser.IsFileDialogActivation(
				"-outputpath \"C:\\Temp\\open.tmp\" -opendialog"));
		}

		[TestMethod]
		public void Does_Not_Treat_Normal_Launches_As_Dialogs()
		{
			Assert.IsFalse(CommandLineParser.IsFileDialogActivation(null));
			Assert.IsFalse(CommandLineParser.IsFileDialogActivation("C:\\Users\\me\\Downloads"));
			Assert.IsFalse(CommandLineParser.IsFileDialogActivation("-outputpath \"C:\\Temp\\result.tmp\""));
		}
	}
}
