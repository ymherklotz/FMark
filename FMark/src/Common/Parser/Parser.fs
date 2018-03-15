module Parser
open Types
open Shared
open ParserHelperFuncs
open Logger

// helper functions


/// parse inline code
let rec parseCode toks =
    match toks with
    | BACKTICK::_ -> ("", toks.[1..]) |> Ok
    | tok::toks' ->
        parseCode toks'
        |> Result.map (fun (str, tks) ->
        mapTok tok + str, tks )
    | e ->  sharedLog.Warn None (sprintf "%A" e)
            ("\\`", xOnwards 1 toks) |> Ok

/// parse a paragraph which counts for contents in  `<p>`
/// parseParagraph eats 2>= ENDLINEs
let parseParagraph toks =
    let parseParagraph' lines tokLine = (parseInLineElements tokLine) :: lines
    toks
    |> trimENDLINEs
    |> cutIntoLines
    |> List.fold parseParagraph' []
    |> List.rev
    |> Paragraph

/// match table start sequence
/// return table rows, terminates when [] or two continuous ENDLINEs
/// start sequence:
/// something in first line, at least one '|' and three '-' in second line
let (|MatchTable|_|) toks =
    // transform table rows into Table or Pretable depending if valid table.
    let tableTransform (rows,rtks) =
        rows |> Markalc.parseEvaluateTable
        |> function
        | Ok(rows) -> 
            let toPCellList (cell:Cell) = 
                let toks,head,align = (cell.GetParams) 
                let pCellLine = toks |> parseInLineElements
                CellLine(pCellLine,head,align)
            let toPRow row = 
                let clst, rHead = row |> function | Cells(clst',rHead') -> clst',rHead'
                PCells(List.map toPCellList clst, rHead)// Create PRows
            // For each row, unpack into Cell list
            (List.map toPRow rows |> Table,rtks) |> Some
        | Error(_)-> None
    match toks with
    | MatchTableHead rtks ->
        match rtks with
        | MatchTableFormater _ -> cutTableRows toks |> tableTransform
        | _ -> None
    | _ -> None

/// parse list
let parseList toks =
    // call itself if list item has a higher level
    // return if list item has lower level
    let ignoreError result = match result with | Ok x -> x | Error x -> x
    let takeAwayWhiteSpaces toks =
            match toks with
            | WHITESPACE n:: rtks -> (n/2, rtks)
            | _ -> (0, toks)
    let excludeSelfSkip x = match x with | None -> None | Some 1 -> None | Some n -> Some (n-1)
    /// return list type, list level, and list content
    let (|GetLIContent|_|) toks =
        // return list level and remaining toks
        let (level, retoks) = takeAwayWhiteSpaces toks
        match retoks with
        | ASTERISK:: WHITESPACE _:: _ | MINUS:: WHITESPACE _:: _ -> // unordered list
            (UL, level, xOnwards 2 retoks) |> Some
        | NUMBER _:: DOT:: WHITESPACE _:: _ ->  // ordered list
            (OL, level, xOnwards 3 retoks) |> Some
        | _ -> None

    let getLIContent toks =
        match toks with
        | GetLIContent result -> result |> Ok
        | _ ->
            let (level, retoks) = takeAwayWhiteSpaces toks
            (UL, level, retoks) |> Error

    /// get all list items in current item level and sub lists
    let rec getCurrentList level listItems lines =
        match lines with
        | line:: reLines ->
            match line |> getLIContent |> ignoreError with
            | (_, liLevel, _) when liLevel >= level -> // list item and sub list item
                getCurrentList level (line::listItems) reLines
            | _ -> listItems |> List.rev
        | [] -> listItems |> List.rev

    let rec parseList' level lines =
        let (listType, depth, _) =
            match List.head lines |> getLIContent with
            | Ok result -> result
            | Error result ->
                globLog.Warn (Some 100) "invalid list item, line does not begin with [*;-;number]\ndefault to UL"
                result
        let listFolder (currentLv, listItems, (skipNo: int option), currentLine) line =
            match skipNo with
            | None ->
                match line |> getLIContent |> ignoreError with
                | (_, level, content) when level=currentLv ->
                    let tLine = content |> parseInLineElements
                    (currentLv, StringItem(tLine)::listItems, None, currentLine+1)
                | (_, level, _) when level>currentLv ->
                    let (listItem, skip) =
                        xOnwards currentLine lines
                        |> getCurrentList (currentLv+1) []
                        |> parseList' (currentLv+1)
                    (currentLv, NestedList(listItem)::listItems, skip |> excludeSelfSkip, currentLine+1)
                | _ -> failwith "list item level < current level, not possible"
            | Some skip ->
                match skip with
                | 1 -> (currentLv, listItems, None, currentLine+1)
                | n when n>1 -> (currentLv, listItems, Some (n-1), currentLine+1)
                | _ -> failwith "negative or zero skip number, not possible"
        List.fold listFolder (level, [], None, 0) lines
        |> (fun (_, lis, _, _) ->
            let doSkip =
                match List.length lines with
                | 0 -> None
                | n -> Some n
            {ListType=listType; ListItem=lis |> List.rev; Depth=depth}, doSkip)
    toks
    |> trimENDLINEs
    |> cutIntoLines
    |> parseList' 0
    |> fst




/// parse supported `ParsedObj`s, turn them into a list
/// assuming each item start at the beginning of the line
/// the returned token head does not have 2>= ENDLINE
let rec parseItem (rawToks: Token list) : Result<ParsedObj * Token list, string> =
    let toks = deleteLeadingENDLINEs rawToks
    match toks with
    | CODEBLOCK (content, lang) :: toks' -> (CodeBlock(content, lang), toks') |> Ok
    | MatchTable (rows, rtks) -> (rows, rtks) |> Ok
    | MatchQuote (content, rtks) ->
        (parseInLineElements content |> Quote , rtks)
        |> Ok
    | MatchHeader (level, content, rtks) ->
        let line = parseInLineElements content
        (Header({HeaderName=line; Level=level},"HEADER STRING NOT IMPLEMENTED"), rtks)
        |> Ok
    | PickoutList (list, retoks) -> (parseList list |> List, retoks) |> Ok
    | PickoutParagraph (par, retoks) ->
        (parseParagraph par, retoks) |> Ok
    | _ -> sprintf "Parse item did not match: %A" toks |> removeChars ["[";"]"] |> Error

and parseItemList toks : Result<ParsedObj list * option<Token list>, string> =
    match (List.isEmpty toks, not (List.exists (function | WHITESPACE(_) | ENDLINE -> false | _ -> true) toks)) with
    | (false,false) -> 
        parseItem toks
        |> Result.bind (fun (pobj, re) ->
            match List.isEmpty re with
            | true -> ([pobj], None) |> Ok
            | false ->
                parseItemList re
                |> Result.map(fun (pobjs, re') ->
                    pobj::pobjs, re' )
        )
    | _ -> ([], None) |> Ok // if tokens are only whitespace or endlines, return no parsedObjs

/// top-level Parser, which the user should use
/// `parse` will either return result monad with either `ParsedObj list` or a string of Error message.
/// Unparsed Tokens will be in the returned in the Error message.
let parse toks =
    parseItemList toks
    |> Result.bind (fun (pobjs, retoks) ->
        match retoks with
        | None -> pobjs |> Ok
        | Some retoks -> sprintf "Some unparsed tokens: %A" retoks |> Error)
