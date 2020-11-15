// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

namespace MyXamarinApp
{
	[Register ("ViewController")]
	partial class ViewController
	{
		[Outlet]
		AppKit.NSTextField _textField { get; set; }

		[Action ("_buttonOne:")]
		partial void _buttonOne (Foundation.NSObject sender);

		[Action ("_buttonTwo:")]
		partial void _buttonTwo (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (_textField != null) {
				_textField.Dispose ();
				_textField = null;
			}
		}
	}
}
