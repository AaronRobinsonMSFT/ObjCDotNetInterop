using System;

using AppKit;
using Foundation;

namespace MyXamarinApp
{
    public partial class ViewController : NSViewController
    {
        private int counter;
        public ViewController(IntPtr handle) : base(handle)
        {
            this.counter = 1;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // Do any additional setup after loading the view.
        }

        public override NSObject RepresentedObject
        {
            get
            {
                return base.RepresentedObject;
            }
            set
            {
                base.RepresentedObject = value;
                // Update the view, if already loaded.
            }
        }

        partial void _buttonOne(NSObject sender)
        {
            this._textField.StringValue = $"Count {++this.counter} (+1)";
        }

        partial void _buttonTwo(NSButton sender)
        {
            this._textField.StringValue = $"Count {this.counter += 2} (+2)";
        }
    }
}
