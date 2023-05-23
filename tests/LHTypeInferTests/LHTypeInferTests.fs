// For emacs: -*- fsharp -*-

module LHTypeInferTests

open NUnit.Framework
open LHExpr
open type LHTypes.Type

[<SetUp>]
let SetupBeforeEachTest () =
    printfn "Executing test: %s" NUnit.Framework.TestContext.CurrentContext.Test.Name

[<Test>]
let testBasic () =
    let se0, se1 = SNum 10, SNull
    let env = Map []
    Assert.AreEqual( (Int 256, Unit), (fst (LHTypeInfer.typeInference env (toAST se0)),
                                       fst (LHTypeInfer.typeInference env (toAST se1))))
[<Test>]
let testLet1 () =
    let sexpr = SLet ("x", SNum 1, SVar "x")
    let env = Map []
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST sexpr)))

[<Test>]
let testAp1 () =
    let se = SAp ( SFunc ("x", SVar "x"), SNum 1 )
    let env = Map []
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST se)))

[<Test>]
let testAdd1 () =
    let se = SAdd (SVar "x", SNum 1)
    let env = Map [("x", LHTypeInfer.Scheme ([],Int 256))]
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST se)))

[<Test>]
let testIf1 () =
    let se = SIf (SGt (SVar "x", SNum 1), SNum 10, SNum 20)
    let env = Map [("x", LHTypeInfer.Scheme ([],Int 256))]
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST se)))

[<Test>]
let testIf2 () =
    let se = SFunc ("x", SIf (SVar "x", SNum 10, SNum 20))
    let env = Map []
    Assert.AreEqual(Function (Bool, Int 256), fst (LHTypeInfer.typeInference env (toAST se)))

[<Test>]
let testIf3 () =
    let se = SFunc ("x", SIf (SVar "x", SFunc ("y", SVar "y"), SFunc ("z", SVar "z")))
    let env = Map []
    match fst (LHTypeInfer.typeInference env (toAST se)) with
        | Function (Bool, Function (TVar s1, TVar s2)) when s1 = s2 ->
            Assert.Pass()
        | _ as p ->
            Assert.Fail( sprintf "incorrect type: %A" p )

[<Test>]
let testIf4 () =
    // fun x -> if x then 10 else x
    let se = SFunc ("x", SIf (SVar "x", SNum 10, SVar "x"))
    let env = Map []
    try
        LHTypeInfer.typeInference env (toAST se) ;
        Assert.Fail( "must not typecheck" )
    with
    | _ ->
        Assert.Pass()

[<Test>]
let testFunc1 () =
    let se = SFunc ("x", SAdd (SVar "x", SNum 1))
    let env = Map []
    Assert.AreEqual(Function (Int 256, Int 256), fst (LHTypeInfer.typeInference env (toAST se)));

[<Test>]
let testAp2 () =
    let se = SAp (SFunc ("x", SAdd (SVar "x", SNum 1)), SNum 10)
    let env = Map []
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST se)) );

[<Test>]
let testAp3 () =
    let se = SAp (SFunc ("x", SAdd (SVar "x", SNum 1)), SNum 10)
    let env = Map []
    Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env (toAST se)));

[<Test>]
let testAp4 () =
    // fun f -> fun n -> f (n + 1)
    let ast = SFunc ("f", SFunc ("n", SAp (SVar "f", SAdd (SVar "n", SNum 1))))
    let env = Map []
    match fst (LHTypeInfer.typeInference env (toAST ast)) with
        | Function (Function (Int 256, TVar s1), Function (Int 256, TVar s2)) when s1 = s2 ->
            Assert.Pass()
        | _ as p ->
            Assert.Fail( sprintf "incorrect type: %A" p )

[<Test>]
let testRecFunc1 () =
    let ast = SLetRec ("fact",
               SFunc ("n",
                SIf (SGt (SVar "n", SNum 1),
                     SMul (SVar "n",
                       SAp (SVar "fact", SSub (SVar "n", SNum 1))),
                     SNum 1)),
               SVar "fact")
    let env = Map []
    Assert.AreEqual(Function (Int 256, Int 256),
                    fst (LHTypeInfer.typeInference env (toAST ast)));

[<Test>]
let testRecFunc2 () =
    // tail-recursive factorial
    // let rec fact n res = if (n > 1) then fact (n-1) (n*res) else res
    let ast =
        SLetRec ("fact",
          SFunc ("n",
           SFunc ("res",
            SIf (SGt (SVar "n", SNum 1),
                 SAp (SAp (SVar "fact", SSub (SVar "n", SNum 1)), SMul (SVar "res", SVar "n")),
                 SNum 1))),
          SVar "fact")
    let env = Map []
    Assert.AreEqual(Function (Int 256, Function (Int 256, Int 256)),
                    fst (LHTypeInfer.typeInference env (toAST ast)));

[<Test>]
let testRecFunc3 () =
    // tail-recursive factorial
    // let rec fact n res = if (n > 1) then fact (n-1) (n*res) else res
    let ast =
        SLetRec ("fact",
          SFunc ("n",
           SFunc ("res",
            SIf (SGt (SVar "n", SNum 1),
                 SAp (SAp (SVar "fact", SSub (SVar "n", SNum 1)), SMul (SVar "res", SVar "n")),
                 SNum 1))),
          SAp (SAp (SVar "fact", SNum 10), SNum 1))
    let env = Map []
    Assert.AreEqual(Int 256,
                    fst (LHTypeInfer.typeInference env (toAST ast)))

[<Test>]
let testEval1 () =
    // f x = x - 1  : Int -> Int
    // main _ = (f 5) : Int
    let ast = SLet ("f", SFunc ("x", SSub (SVar "x", SNum 1)),
                 SLet ("main", SAp (SVar "f", SNum 5), SVar "main"))
    let env = Map []
    Assert.AreEqual(Int 256,
                    fst (LHTypeInfer.typeInference env (toAST ast)));
[<Test>]
let testEvalCurry1 () =
    // f = \f1 -> \f2 -> \x -> \y -> f2 (f1 x) (f1 y)
    let f cont =
        SLet ("f",
          SFunc ("f1",
           SFunc ("f2",
            SFunc ("x",
             SFunc ("y", SAp (SAp (SVar "f2", SAp (SVar "f1", SVar "x")),
                                    SAp (SVar "f1", SVar "y")))))), cont)
    // sum = \x -> \y -> x + y
    let sum cont =
        SLet ("sum",
          SFunc ("x",
           SFunc ("y",
            SAdd (SVar "x", SVar "y"))), cont)
    // let inc = \x -> x + 1
    let inc cont =
        SLet ("inc",
          SFunc ("x",
           SAdd (SVar "x", SNum 1)), cont)

    // main = f inc sum 10 20 = sum (inc 10) (inc 20) : Int
    let main = SLet ("main",
                SAp (SAp (SAp (SAp (SVar "f", SVar "inc"), SVar "sum"), SNum 10), SNum 20),
                SVar "main")
    let ast = f (sum (inc main))
    let env = Map []
    Assert.AreEqual(Int 256,
                    fst (LHTypeInfer.typeInference env (toAST ast)));

[<Test>]
let testCurryInc () =
  // let main =
  //  let apply func x = (func x) in
  //  let inc x = x + 1 in
  //     ((apply inc) 1) ;;"
  let sexpr =
      SLet ("apply",
        SFunc ("func",
          SFunc ("x", SAp (SVar "func", SVar "x"))),
        SLet ("inc",
          SFunc ("x", SAdd (SVar "x", SNum 1)),
            SAp (SAp (SVar "apply", SVar "inc"), SNum 1)))
  let env = Map []
  let ast = (toAST sexpr)
  Assert.AreEqual(Int 256, fst (LHTypeInfer.typeInference env ast))
