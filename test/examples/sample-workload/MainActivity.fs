namespace sample_workload

open Android.App
open Android.Content
open Android.OS
open Xamarin.Android

[<Activity(Label = "@string/app_name", MainLauncher = true)>]
type MainActivity() =
    inherit Activity()

    override x.OnCreate(savedInstanceState: Bundle) =
        ``base``.OnCreate(savedInstanceState)
        ``base``.SetContentView(Resource.Layout.activity_main)
