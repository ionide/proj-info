module Railway
open System

type Attempt<'S,'F> = (unit -> Result<'S,'F>)

let private succeed x = (fun () -> Ok x)
let private failed err = (fun () -> Error err)
let runAttempt (a: Attempt<_,_>) = a ()
let private delay f = (fun () -> f() |> runAttempt)
let private bind successTrack (input : Attempt<_, _>) = 
    match runAttempt input with
    | Ok s -> successTrack s
    | Error f -> failed f

type AttemptBuilder() =
    member this.Bind(m : Attempt<_, _>, success) = bind success m
    member this.Bind(m : Result<_, _>, success) = bind success (fun () -> m)
    member this.Bind(m : Result<_, _> option, success) = 
        match m with
        | None -> this.Combine(this.Zero(), success)
        | Some x -> this.Bind(x, success)
    member this.Return(x) : Attempt<_, _> = succeed x
    member this.ReturnFrom(x : Attempt<_, _>) = x
    member this.Combine(v, f) : Attempt<_, _> = bind f v
    member this.Yield(x) = Ok x
    member this.YieldFrom(x) = x
    member this.Delay(f) : Attempt<_, _> = delay f
    member this.Zero() : Attempt<_, _> = succeed ()
    member this.While(guard, body: Attempt<_, _>) =
        if not (guard()) 
        then this.Zero() 
        else this.Bind(body, fun () -> 
            this.While(guard, body))  

    member this.TryWith(body, handler) =
        try this.ReturnFrom(body())
        with e -> handler e

    member this.TryFinally(body, compensation) =
        try this.ReturnFrom(body())
        finally compensation() 

    member this.Using(disposable:#System.IDisposable, body) =
        let body' = fun () -> body disposable
        this.TryFinally(body', fun () -> 
            match disposable with 
                | null -> () 
                | disp -> disp.Dispose())

    member this.For(sequence:seq<'a>, body: 'a -> Attempt<_,_>) =
        this.Using(sequence.GetEnumerator(),fun enum -> 
            this.While(enum.MoveNext, 
                this.Delay(fun () -> body enum.Current)))

let attempt = new AttemptBuilder()
