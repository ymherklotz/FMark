module RefParse
open Types

let monthConv m =
    match m with
    | 1 -> Some "January"
    | 2 -> Some "February"
    | 3 -> Some "March"
    | 4 -> Some "April"
    | 5 -> Some "May"
    | 6 -> Some "June"
    | 7 -> Some "July"
    | 8 -> Some "August"
    | 9 -> Some "September"
    | 10 -> Some "October"
    | 11 -> Some "November"
    | 12 -> Some "December"
    | _ -> None

let ordinalConv d =
    let (|OrdinalDates|_|) = function
        | d when d > 31 || d < 1 -> None
        | 1 | 21 | 31 -> Some "st"
        | 2 | 22 -> Some "nd"
        | 3 | 23 -> Some "rd"
        | _ -> Some "th"
    match d with
    | OrdinalDates s -> Some (string d + s)
    | _ -> None

let yerGen style year =
    match year with
    | None -> []
    | Some a -> 
        match style with
        | Harvard -> ["(" + string a + ") "|> Literal |> FrmtedString]
        | Chicago -> [string a + ". " |> Literal |> FrmtedString]
        | IEEE -> []

let urlGen style url =
    match url with
    | None -> []
    | Some a ->
        match style with
        | Harvard -> 
            [FrmtedString (Literal "Available from: "); Link(Literal a,a);
                FrmtedString (Literal " ")]
        | Chicago -> [Link(Literal a,a)]
        | IEEE -> []

let plnGen tokLst =
    let rec plainGen' tLst =
        match tLst with
        | LITERAL lit::tl -> lit::plainGen' tl
        | WHITESPACE _::tl -> " "::plainGen' tl
        | _::tl -> plainGen' tl
        | [] -> []
    plainGen' tokLst |> List.rev |> List.reduce (+)

let dateGen style date =
    match date with
    | None -> []
    | Some (y,m,d) ->
        // check validity of date (Not complete, e.g. Feb-30 passes thru)
        let mstr = monthConv m
        let dstr = ordinalConv d
        match style with
        | Harvard ->
            match mstr, dstr with
            | Some mm, Some dd ->
                ["[Accessed "+dd+" "+mm+" "+(string y)+"]. "
                    |> Literal |> FrmtedString]
            | _,_ -> ["Access date invalid, please use yyyy-mm-dd"
                        |> Literal |> FrmtedString]
        | Chicago ->
            match mstr, dstr with
            | Some mm, Some _ ->
                ["Accessed "+mm+" "+(string d)+", "+(string y)+". "
                    |> Literal |> FrmtedString]
            | _,_ -> ["Access date invalid, please use yyyy-mm-dd"
                        |> Literal |> FrmtedString]
        | IEEE -> []

type GenType = HarAut | ChiAut | ChiBookTil | ChiWebTil | HarTil
let (|OverallM|) =
    let hAut tokLst =
        let rec hAutGen' tLst:TLine =
            match tLst with
            | LITERAL lit::tl ->
                match tl with
                | [] -> [FrmtedString (Literal (lit+", "))]
                | _ -> FrmtedString (Literal (string lit.[0]+". "))::hAutGen' tl
            | _::tl -> hAutGen' tl
            | [] -> []
        List.rev tokLst |> hAutGen' |> List.rev
    let cAut tokLst = [plnGen tokLst + ". " |> Literal |> FrmtedString]
    let cWebTil tokLst = ["\"" + plnGen tokLst + ".\" " |> Literal |> FrmtedString]
    let cTil tokLst = [[plnGen tokLst + ". " |> Literal |> FrmtedString] |> Emphasis |> FrmtedString]
    let hTil tokLst = [[plnGen tokLst + ". " |> Literal |> FrmtedString] |> Emphasis |> FrmtedString]
    function
    | HarAut -> hAut
    | HarTil -> hTil
    | ChiAut -> cAut
    | ChiBookTil -> cTil
    | ChiWebTil -> cWebTil

let build gType tokLst =
    match tokLst with
    | None -> []
    | Some tl ->
        match gType with
        | OverallM f -> f tl

let refInLine style ref:TLine =
    match ref.Author, ref.Year with
    | Some a, Some y ->
        let surname = (List.rev a).Head
        // this part is weirddddd
        match surname with
        | LITERAL lit ->
            match style with
            | IEEE -> []
            | Chicago ->
                ["(" + lit + ", " + string(y) + ")" |> Literal |> FrmtedString]
            | Harvard ->
                ["(" + lit + " " + string(y) + ")" |> Literal |> FrmtedString]
        | _ -> []
    | _,_ -> []

let ref2TLine format ref:TLine =
    match format with
    | IEEE -> [FrmtedString (Literal "IEEE citation not supported yet")]
    | Chicago ->
        match ref.Cat with
        | Some Book ->
            [build ChiAut ref.Author; yerGen Chicago ref.Year;
                build ChiBookTil ref.Title]
            |> List.reduce List.append
        | Some Website ->
            [build ChiAut ref.Author; yerGen Chicago ref.Year; build ChiWebTil ref.Title;
                dateGen Chicago ref.AccessDate; urlGen Chicago ref.URL]
            |> List.reduce List.append
        | None -> [FrmtedString (Literal "Please specify type of reference")]
    | Harvard ->
        [build HarAut ref.Author; yerGen Harvard ref.Year; build HarTil ref.Title;
            urlGen Harvard ref.URL; dateGen Harvard ref.AccessDate]
        |> List.reduce List.append

// parses a single reference entry
// This probably should never see ENDLINE
let refParser style tLst =
    let rec refPar' refData tLst =
        let rec refParse' parsing tail =
            match tail with
            | COMMA::tl -> parsing, tl
            | ENDLINE::tl -> parsing, tl
            | a::tl -> refParse' (a::parsing) tl
            | [] -> parsing, []

        let dateFormat tail =
            match tail with
            | NUMBER y::MINUS::NUMBER m::MINUS::NUMBER d::tl ->
                Some (int y, int m, int d), tl
            | _ -> None, tail

        match tLst with
        | LITERAL "type"::EQUAL::WHITESPACE _::LITERAL t::tl -> 
            match t with
            | "Book" -> refPar' {refData with Cat = Some Book} tl
            | "Website" -> refPar' {refData with Cat = Some Website} tl
            | _ -> refPar' refData tl
        | LITERAL "author"::EQUAL::WHITESPACE _::tl ->
            refParse' [] tl
            |> fun (x,y) -> refPar' {refData with Author = Some x} y
        | LITERAL "title"::EQUAL::WHITESPACE _::tl ->
            refParse' [] tl
            |> fun (x,y) -> refPar' {refData with Title = Some x} y
        | LITERAL "year"::EQUAL::WHITESPACE _::NUMBER a::tl ->
            refPar' {refData with Year = Some (int a)} tl
        | LITERAL "url"::EQUAL::WHITESPACE _::LITERAL s::tl ->
            refPar' {refData with URL = Some s} tl
        | LITERAL "access"::EQUAL::WHITESPACE _::tl ->
            dateFormat tl
            |> fun (x,y) -> refPar' {refData with AccessDate = x} y
        | ENDLINE::tl -> refData,tl
        | _::tl -> refPar' refData tl
        | [] -> refData, []
    tLst    
    |> refPar' {Cat = None; Author = None; Title = None;
                    Year = None; AccessDate = None; URL = None}
    |> fun (x,_) -> refInLine style x, ref2TLine style x


// parse references with refParser
let refParse style tocLst =
    let ind = tocLst |> List.tryFindIndex (fun x -> x = ENDLINE)
    match ind with
    | Some i ->
        let (h,t) = List.splitAt i tocLst
        refParser style h |> fun (a,b) -> a,b,t.Tail
    | None ->
        refParser style tocLst |> fun (a,b) -> a,b,[]
