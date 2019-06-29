namespace l1

open Newtonsoft.Json

module Say =

    type Movie = {
        Name : string
        Year: int
    }

    let jsonstuff () =

        let movies = [
            { Name = "Bad Boys"; Year = 1995 };
            { Name = "Bad Boys 2"; Year = 2003 }
        ]
    
        let json = JsonConvert.SerializeObject(movies)
        printfn "%s" json
