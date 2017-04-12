namespace l2

open Newtonsoft.Json

type Movie = {
    Name : string
    Year: int
}

module Say =
    let hello name =
        printfn "Hello %s" name
