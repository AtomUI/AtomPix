# AtomUI Labs ImageGallery

Experimental immersive image collection viewer for AtomUI and Avalonia applications.

## Install

~~~shell
dotnet add package AtomUI.Labs.Controls.ImageGallery
~~~

Use the package version that matches the AtomUI packages in the application.

## Application setup

~~~csharp
this.UseAtomUI(builder => builder.UseImageGallery());
~~~

## AXAML

~~~xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:atom.labs="https://atomui.net/labs">
    <atom.labs:ImageGallery ItemsSource="{CompiledBinding Images}" />
</Window>
~~~

The package depends on AtomUI.Core and Avalonia. It does not depend on AtomUI Desktop Controls or GalleryBase.
