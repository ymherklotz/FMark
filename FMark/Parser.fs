module Parser
open Types


// helper functions

/// count continuous spaces
let rec countSpaces toks =
    match toks with
    | WHITESPACE n :: toks' -> countSpaces toks' |> (+) n
    | _ -> 0

let rec countENDLINEs toks =
    match toks with
    | ENDLINE :: toks' -> countENDLINEs toks' |> (+) 1
    | _ -> 0

let parseLiteral toks =
    let rec parseLiteral' toks str =
        let appendString newstr sep retoks =
            match String.length str with
            | 0 -> [] | _ -> [str]
            |> (fun sl -> List.append sl [newstr])
            |> String.concat sep |> parseLiteral' retoks
        match toks with
        | LITERAL str' :: toks' -> appendString str' " " toks'
        | WHITESPACE _ :: LITERAL str' :: toks' -> appendString str' " " toks'
        | ENDLINE::LITERAL str'::toks' -> appendString str' " " toks'
        | _ -> str, toks
    parseLiteral' toks ""

let parseInLineElements toks =
    let rec parseInLineElements' toks =
        let leadingSpaces = countSpaces toks
        match toks with
        | t when List.isEmpty t -> [], []
        | _ when leadingSpaces >=2 -> [], toks.[leadingSpaces..] // new TLine equivalent <br>
        | ENDLINE :: toks' -> parseInLineElements' toks'
        | LITERAL _ :: _ ->
            let pstr, retoks = parseLiteral toks
            let inlines, retoks' = parseInLineElements' retoks
            FrmtedString (Literal pstr) :: inlines, retoks'
        | _ -> failwithf "this inline element is not implmented"
    parseInLineElements' toks

/// parseParagraph eats ENDLINE
let parseParagraph toks =
    let rec parseParagraph' toks =
        match toks with
        | t when List.isEmpty t -> [], []
        | LITERAL _::toks' ->
            let inlines, retoks = parseInLineElements toks
            let lines, retoks' = parseParagraph' retoks
            inlines:: lines, retoks'
        | ENDLINE::toks' -> [], toks'
        | _ -> failwithf "parseParagraph ele not implemented"
    let prep, retoks = parseParagraph' toks
    (Paragraph prep, retoks)


let rec parseItem (toks: Token list) : Result<ParsedObj * Token list, string> =
    match toks with
    | CODEBLOCK (content, lang) :: toks' -> (CodeBlock(content, lang), toks') |> Ok
    | ENDLINE _ :: NUMBER _ :: DOT :: WHITESPACE _ :: toks' -> "Lists todo" |> Error
    | LITERAL str :: toks' ->
        // match parseLiteral toks with
        // | lstr, retoks -> (Paragraph([[FrmtedString(Literal lstr)]]), retoks) |> Ok
        parseParagraph toks |> Ok
    | _ -> "not implemented" |> Error

and parseItemList toks : Result<ParsedObj list * option<Token list>, string> =
    parseItem toks
    |> Result.bind (fun (pobj, re) ->
        match List.isEmpty re with
        | true -> ([pobj], None) |> Ok
        | false ->
            parseItemList re
            |> Result.map(fun (pobjs, re') ->
                pobj::pobjs, re' )
        )

let parse toks =
    parseItemList toks
    |> Result.bind (fun (pobjs, retoks) ->
        match retoks with
        | None -> pobjs |> Ok
        | Some retoks -> "Some unparsed tokens" |> Error)