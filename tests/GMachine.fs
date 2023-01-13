module GMachine

open NUnit.Framework
open System
open System.Collections.Generic

exception GMError of string

type Name = string

type Instruction =
    | Unwind
    | Pushglobal of name: Name
    | Pushint of v: int
    | Push of e: int
    | Mkap
    | Slide of n: int

type Addr = int

type GmCode = Instruction list

let getCode (i, _, _, _, _) =
    i
let putCode i' (i, stack, heap, globals, stats) =
    (i', stack, heap, globals, stats)

type GmStack = Addr list

let getStack (i, stack, heap, globals, stats) =
    stack

let putStack s' (i, stack, heap, globals, stats) =
    (i, s', heap, globals, stats)

// Expression node (when computing, not AST)
type Node =
    | NNum of v: int
    | NAp of f: Addr * a: Addr // f(a)
    | NGlobal of args: int * code: GmCode

type GmHeap = Map<Addr, Node>

let getHeap (i, stack, heap, globals, stats) =
    heap
let putHeap h' (i, stack, heap, globals, stats) =
    (i, stack, h', globals, stats)

type GmGlobals = Map<Name, Addr>

let getGlobals (i, stack, heap, globals, stats) =
    globals
type GmStats = int

let statInitial =
    0
let statIncSteps s =
    s + 1
let statGetSteps s =
    s

let getStats (i, stack, heap, globals, stats) =
    stats
let putStats stats' (i, stack, heap, globals, stats) =
    (i, stack, heap, globals, stats')

type GmState =
    GmCode * GmStack * GmHeap * GmGlobals * GmStats

// Evaluator:
// test that we are in the final state, no more steps to do
let gmFinal s =
    match (getCode s) with
    | [] -> true
    | _ -> false

// increment steps statistics
let doAdmin s =
    putStats (statIncSteps (getStats s)) s

let heapAlloc heap node =
    let findNewAddr heap =
        if Map.isEmpty heap then
            0
        else
            (List.max (Map.keys heap |> Seq.cast |> List.ofSeq)) + 1
    let addr = findNewAddr heap
    (Map.add addr node heap, addr)

let pushglobal (f:Name) (state:GmState) =
    match Map.tryFind f (getGlobals state) with
        | Some a ->
            putStack (a :: getStack state) state
        | None ->
            let msg = sprintf "Global name %A not found in the globals dictionary" f
            raise (GMError msg)

let pushint n state =
    let (heap', a) = heapAlloc (getHeap state) (NNum n)
    putHeap heap' (putStack (a :: getStack state) state)

let mkap state =
    match getStack state with
        | a1 :: a2 :: as' ->
            let (heap', a) = heapAlloc (getHeap state) (NAp (a1, a2))
            putHeap heap' (putStack (a :: as') state)
        | _ ->
            raise (GMError "stack underflow")

let getApArg n =
    match n with
        | NAp (f, v) ->
            v
        | _ ->
            raise (GMError "node must be of NAp type")

let heapLookup (heap:GmHeap) (key:Addr) : Node =
    match Map.tryFind key heap with
        | Some v ->
            v
        | None ->
            let msg = sprintf "key %A not found in the map" key
            raise (GMError msg)

let at l n =
    List.item n l

let push n state =
    let as' = getStack state
    let a = getApArg (heapLookup (getHeap state) (at as' (n + 1)))
    putStack (a :: as') state

let slide n state =
    match getStack state with
        | a :: as' ->
            putStack (a :: List.skip n as') state
        | _ ->
            raise (GMError "stack underflow")

let unwind state =
    match getStack state with
        | a :: as' ->
            let heap = getHeap state
            let newState s =
                match s with
                    | NNum n ->
                        state
                    | NAp (a1, a2) ->
                        putCode [Unwind] (putStack (a1 :: a :: as') state)
                    | NGlobal (n, c) ->
                        if List.length as' < n then
                            raise (GMError "Unwinding with too few arguments")
                        else
                            putCode c state
            newState (heapLookup heap a)
        | _ ->
            raise (GMError "stack underflow")

let dispatch i =
    match i with
        | Pushglobal f ->
            pushglobal f
        | Pushint n ->
            pushint n
        | Mkap ->
            mkap
        | Push n ->
            push n
        | Slide n ->
            slide n
        | Unwind ->
            unwind

// there is always at least one instruction in the code
// otherwise the step function shouldn't have executed
let step state =
    match getCode state with
        | i :: is ->
            dispatch i (putCode is state)
        | _ ->
            raise (GMError "stack underflow")

// new state is added to the end of the list
let rec eval state =
    let restStates =
        if gmFinal state then [] else
            let nextState = doAdmin (step state)
            eval nextState
    state :: restStates

// AST Expression node
type Expr =
    | EVar of name:Name
    | ENum of n:int
    | EAp of e1:Expr * e2:Expr

type GmCompiledSC = Name * int * GmCode

let initialCode : GmCode =
    [Pushglobal "main"; Unwind]

// Allocate supercombinator, i.e. add the given new supercombinator
// into the heap and return newly allocated address together with
// the new heap. This is a folding function.
let allocateSc (heap: GmHeap, globals: GmGlobals) ((name, nargs, code):GmCompiledSC) =
    let (heap', addr) = heapAlloc heap (NGlobal (nargs, code))
    let globals' = Map.add name addr globals
    (heap', globals')

// shift all indexes on m places
let argOffset m env =
    [for (n, v) in env -> (n + m, v)]

let rec compileC ast env =
    match ast with
        | ENum n ->
            [Pushint n]
        | EAp (e1, e2) ->
            compileC e2 env @ compileC e1 (argOffset 1 env) @ [Mkap]
        | EVar v ->
            let r = List.tryPick (fun (n, v') ->
                                  if v' = v then Some n else None) env
            match r with
                | Some n ->
                    [Push n]
                | _ ->
                    [Pushglobal v]

// ast env -> Instruction list
let compileR ast env =
    compileC ast env @ [Slide (List.length env + 1); Unwind]

// Supercombinator is defined by the following triplet
// (sc name, list of formal argument variable names, body AST)
type SC = Name * (Name list) * Expr

// Program is a list of supercombinator definitions, including the
// Main combinator
type CoreProgram = SC list

// compile Supercombinator with the given name, having the
// given environment and ast (body)
let compileSc ((name, env, ast): SC) : GmCompiledSC =
    (name, List.length env, compileR ast (List.indexed env))

// (GmHeap, GmGlobals)
let buildInitialHeap program =
    let initialHeap = Map []
    let initialGlobals = Map []
    let acc = (initialHeap, initialGlobals)
    let compiled1 = List.map compileSc program
    let (heap, globals) = List.fold allocateSc acc compiled1
    (heap, globals)

let compile (program: CoreProgram) : GmState =
    let (heap, globals) = buildInitialHeap program
    (initialCode, [], heap, globals, statInitial)

let getResult (st:GmState) : Node =
    match st with
        | ([], [resultAddr], heap, _, _) ->
            heapLookup heap resultAddr
        | _ ->
            raise (GMError "incorrect VM final state")

[<OneTimeSetUp>]
let Setup () =
    ()

[<Test>]
let compileScKTest () =
    let code = compileSc ("K", ["x"; "y"], (EVar "x"))
    Assert.AreEqual( ("K", 2, [Push 0; Slide 3; Unwind]), code );

[<Test>]
let compileScFTest () =
    let code = compileSc ("F", ["x"; "y"], (EAp (EVar "z", EVar "x")))
    Assert.AreEqual( ("F", 2, [Push 0; Pushglobal "z"; Mkap; Slide 3; Unwind]), code );

[<Test>]
let compileProgTest () =
    let coreProg =
        [ ("main", [], (EAp (EVar "Z", EVar "y")));
          ("y", [], (ENum 1));
          ("Z", ["x"], (EVar "x")) ]
    let initSt = compile coreProg
    let finalSt = List.last (eval initSt)
    printfn "%A" finalSt
    Assert.AreEqual( NNum 1, getResult finalSt )
