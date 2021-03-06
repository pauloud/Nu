﻿namespace InfinityRpg
open System
open System.Numerics
open Prime
open Nu
open Nu.Declarative
open InfinityRpg

type [<ReferenceEquality; NoComparison>] MapModeler =
    { FieldMapUnits : Map<Vector2i, FieldMapUnit>
      CurrentFieldOffset : Vector2i }

    static member empty =
        { FieldMapUnits = Map.empty
          CurrentFieldOffset = v2iZero }

    member this.AddFieldMapUnit fieldMapUnit =
        let fieldMapUnits = Map.add fieldMapUnit.OffsetCount fieldMapUnit this.FieldMapUnits
        { this with FieldMapUnits = fieldMapUnits; CurrentFieldOffset = fieldMapUnit.OffsetCount }

    member this.Current =
        this.FieldMapUnits.[this.CurrentFieldOffset]

    member this.OffsetInDirection direction =
        this.CurrentFieldOffset + dtovm direction
    
    member this.ExistsInDirection direction =
        Map.containsKey (this.OffsetInDirection direction) this.FieldMapUnits
    
    member this.NextOffset =
        if this.Current.IsHorizontal
        then this.OffsetInDirection Rightward
        else this.OffsetInDirection Upward
    
    member this.NextOffsetInDirection direction =
        this.NextOffset = this.OffsetInDirection direction

    member this.PossibleInDirection direction =
        this.ExistsInDirection direction || this.NextOffsetInDirection direction
    
    member this.MoveCurrent direction =
        { this with CurrentFieldOffset = this.OffsetInDirection direction }
    
    member this.MakeFieldMapUnit =
        this.AddFieldMapUnit (FieldMapUnit.make (Some this.Current))
    
    member this.Transition direction =
        if this.ExistsInDirection direction
        then this.MoveCurrent direction
        else this.MakeFieldMapUnit
    
    static member make =
        MapModeler.empty.AddFieldMapUnit (FieldMapUnit.make None)

type [<NoComparison>] Move =
    | Step of Direction
    | Attack of CharacterIndex
    | Travel of NavigationNode list

    member this.MakeActivity positionM =
        match this with
        | Step direction -> CharacterActivityState.makeNavigation false positionM direction
        | Attack index -> CharacterActivityState.makeAttack index
        | Travel path ->
            let direction = Math.directionToTarget positionM path.Head.PositionM
            CharacterActivityState.makeNavigation true positionM direction

    member this.TruncatePath =
        match this with
        | Travel (head :: _) -> Travel [head]
        | _ -> this

type [<ReferenceEquality; NoComparison>] Chessboard =
    { PassableCoordinates : Map<Vector2i, PickupType Option>
      CharacterCoordinates : Map<CharacterIndex, Vector2i>
      CurrentMoves : Map<CharacterIndex, Move> }

    static member empty =
        { PassableCoordinates = Map.empty
          CharacterCoordinates = Map.empty
          CurrentMoves = Map.empty }

    member this.EnemyCoordinates =
        Map.filter (fun (k : CharacterIndex) _ -> k.IsEnemy) this.CharacterCoordinates

    member this.PlayerCoordinates =
        Map.filter (fun (k : CharacterIndex) _ -> not k.IsEnemy) this.CharacterCoordinates
    
    member this.PickupItems =
        Map.filter (fun _ v -> v <> None) this.PassableCoordinates

    member this.EnemyCount =
        this.EnemyCoordinates.Count

    member this.PickupCount =
        this.PickupItems.Count
    
    member this.AvailableCoordinates =
        let occupiedCoordinates = Map.toValueSeq this.CharacterCoordinates
        let passableCoordinates = Map.toKeyList this.PassableCoordinates
        List.except occupiedCoordinates passableCoordinates

    member this.OpenDirections coordinates =
        List.filter (fun d -> List.exists (fun x -> x = (coordinates + (dtovm d))) this.AvailableCoordinates) [Upward; Rightward; Downward; Leftward]
    
    static member updatePassableCoordinates updater chessboard =
        { chessboard with PassableCoordinates = updater chessboard.PassableCoordinates }
    
    static member updateCharacterCoordinates updater chessboard =
        { chessboard with CharacterCoordinates = updater chessboard.CharacterCoordinates }

    static member updateCurrentMoves updater chessboard =
        { chessboard with CurrentMoves = updater chessboard.CurrentMoves }

    static member characterExists index chessboard =
        Map.exists (fun k _ -> k = index) chessboard.CharacterCoordinates
    
    static member pickupAtCoordinates coordinates chessboard =
        match chessboard.PassableCoordinates.[coordinates] with
        | Some _ -> true
        | None -> false
    
    static member updateCoordinatesValue newValue coordinates chessboard =
        let passableCoordinates = Map.add coordinates newValue chessboard.PassableCoordinates
        Chessboard.updatePassableCoordinates (constant passableCoordinates) chessboard
    
    static member clearPickups _ _ chessboard =
        let passableCoordinates = Map.map (fun _ _ -> None) chessboard.PassableCoordinates
        Chessboard.updatePassableCoordinates (constant passableCoordinates) chessboard
    
    // used for both adding and relocating
    static member placeCharacter index coordinates (chessboard : Chessboard) =
        if List.exists (fun x -> x = coordinates) chessboard.AvailableCoordinates then
            let characterCoordinates = Map.add index coordinates chessboard.CharacterCoordinates
            Chessboard.updateCharacterCoordinates (constant characterCoordinates) chessboard
        else failwith "character placement failed; coordinates unavailable"

    static member removeCharacter index _ chessboard =
        let characterCoordinates = Map.remove index chessboard.CharacterCoordinates
        Chessboard.updateCharacterCoordinates (constant characterCoordinates) chessboard
    
    static member clearEnemies _ _ (chessboard : Chessboard) =
        Chessboard.updateCharacterCoordinates (constant chessboard.PlayerCoordinates) chessboard
    
    static member addMove index move chessboard =
        let currentMoves = Map.add index move chessboard.CurrentMoves
        Chessboard.updateCurrentMoves (constant currentMoves) chessboard
    
    static member removeMove index _ chessboard =
        let currentMoves = Map.remove index chessboard.CurrentMoves
        Chessboard.updateCurrentMoves (constant currentMoves) chessboard
    
    static member truncatePlayerPath _ _ chessboard =
        let move = chessboard.CurrentMoves.[PlayerIndex].TruncatePath
        Chessboard.addMove PlayerIndex move chessboard
    
    static member setPassableCoordinates _ fieldMap chessboard =
        let passableCoordinates = fieldMap.FieldTiles |> Map.filter (fun _ fieldTile -> fieldTile.TileType = Passable) |> Map.map (fun _ _ -> None)
        Chessboard.updatePassableCoordinates (constant passableCoordinates) chessboard                    

type [<ReferenceEquality; NoComparison>] Gameplay =
    { MapModeler : MapModeler
      Chessboard : Chessboard
      ShallLoadGame : bool
      Field : Field
      Pickups : Pickup list
      Enemies : Character list
      Player : Character }

    static member initial =
        { MapModeler = MapModeler.make
          Chessboard = Chessboard.empty
          ShallLoadGame = false
          Field = Field.initial
          Pickups = []
          Enemies = []
          Player = Character.initial }

    member this.PickupCount =
        this.Pickups.Length

    member this.EnemyCount =
        this.Enemies.Length
    
    static member updateMapModeler updater gameplay =
        { gameplay with MapModeler = updater gameplay.MapModeler }
    
    static member updateField updater gameplay =
        { gameplay with Field = updater gameplay.Field }

    static member updatePickups updater gameplay =
        { gameplay with Pickups = updater gameplay.Pickups }
    
    static member updateEnemies updater gameplay =
        { gameplay with Enemies = updater gameplay.Enemies }

    static member updatePlayer updater gameplay =
        { gameplay with Player = updater gameplay.Player }
    
    static member getCharacters gameplay =
        gameplay.Player :: gameplay.Enemies
    
    static member pickupAtCoordinates coordinates gameplay =
        gameplay.Pickups |> List.exists (fun pickup -> pickup.Position = vmtovf coordinates)

    static member characterExists index gameplay =
        Gameplay.getCharacters gameplay |> List.exists (fun gameplay -> gameplay.Index = index)
    
    static member tryGetCharacterByIndex index gameplay =
        Gameplay.getCharacters gameplay |> List.tryFind (fun gameplay -> gameplay.Index = index)
    
    static member getCharacterByIndex index gameplay =
        Gameplay.tryGetCharacterByIndex index gameplay |> Option.get

    static member getCharacterState index gameplay =
        (Gameplay.getCharacterByIndex index gameplay).CharacterState
    
    static member getTurnStatus index gameplay =
        (Gameplay.getCharacterByIndex index gameplay).TurnStatus
    
    static member getCharacterActivityState index gameplay =
        (Gameplay.getCharacterByIndex index gameplay).CharacterActivityState

    static member getCharacterAnimationState index gameplay =
        (Gameplay.getCharacterByIndex index gameplay).CharacterAnimationState
    
    static member getPosition index gameplay =
        (Gameplay.getCharacterByIndex index gameplay).Position
    
    static member getEnemyIndices gameplay =
        List.map (fun gameplay -> gameplay.Index) gameplay.Enemies

    static member getOpponentIndices index gameplay =
        match index with
        | PlayerIndex -> Gameplay.getEnemyIndices gameplay
        | _ -> [PlayerIndex]
    
    static member getCharacterTurns gameplay =
        Gameplay.getCharacters gameplay |> List.map (fun character -> character.TurnStatus)
    
    static member anyTurnsInProgress gameplay =
        Gameplay.getCharacterTurns gameplay |> List.exists (fun turnStatus -> turnStatus <> Idle)
    
    static member updateCharacterBy by index updater gameplay =
        match index with
        | PlayerIndex ->
            let player = by updater gameplay.Player
            Gameplay.updatePlayer (constant player) gameplay
        | EnemyIndex _ as index ->
            let enemies =
                gameplay.Enemies |>
                List.map (fun enemy -> if enemy.Index = index then by updater enemy else enemy)
            Gameplay.updateEnemies (constant enemies) gameplay
    
    static member updateCharacterState index updater gameplay =
        Gameplay.updateCharacterBy Character.updateCharacterState index updater gameplay
    
    static member updateTurnStatus index updater gameplay =
        Gameplay.updateCharacterBy Character.updateTurnStatus index updater gameplay
    
    static member updateCharacterActivityState index updater gameplay =
        Gameplay.updateCharacterBy Character.updateCharacterActivityState index updater gameplay

    static member updateCharacterAnimationState index updater gameplay =
        Gameplay.updateCharacterBy Character.updateCharacterAnimationState index updater gameplay

    static member updatePosition index updater gameplay =
        Gameplay.updateCharacterBy Character.updatePosition index updater gameplay
    
    static member getCoordinates index gameplay =
        gameplay.Chessboard.CharacterCoordinates.[index]

    static member getIndexByCoordinates coordinates gameplay =
        Map.findKey (fun _ x -> x = coordinates) gameplay.Chessboard.CharacterCoordinates

    static member getCurrentMove index gameplay =
        gameplay.Chessboard.CurrentMoves.[index]
    
    static member createPlayer gameplay =
        let coordinates = Gameplay.getCoordinates PlayerIndex gameplay
        let player = Character.makePlayer coordinates
        Gameplay.updatePlayer (constant player) gameplay

    // a basic sync mechanism that relies on never adding and removing *at the same time*
    static member syncLists (gameplay : Gameplay) =
        let chessboard = gameplay.Chessboard
        let gameplay =
            if gameplay.PickupCount <> chessboard.PickupCount then
                let pickups =
                    if gameplay.PickupCount > chessboard.PickupCount then
                        List.filter (fun (pickup : Pickup) -> Chessboard.pickupAtCoordinates (vftovm pickup.Position) chessboard) gameplay.Pickups
                    else 
                        let generator k _ = Pickup.makeHealth k
                        let pickups = Map.filter (fun k _ -> not (Gameplay.pickupAtCoordinates k gameplay)) chessboard.PickupItems |> Map.toListBy generator
                        pickups @ gameplay.Pickups
                Gameplay.updatePickups (constant pickups) gameplay
            else gameplay

        if gameplay.EnemyCount <> chessboard.EnemyCount then
            let enemies =
                if gameplay.EnemyCount > chessboard.EnemyCount then
                    List.filter (fun (character : Character) -> Chessboard.characterExists character.Index chessboard) gameplay.Enemies
                else
                    let generator k v = Character.makeEnemy k v
                    let enemies = Map.filter (fun k _ -> not (Gameplay.characterExists k gameplay)) chessboard.EnemyCoordinates |> Map.toListBy generator
                    enemies @ gameplay.Enemies
            Gameplay.updateEnemies (constant enemies) gameplay
        else gameplay

    // if updater takes index, index is arg1; if updater takes coordinates, coordinates is arg2
    static member updateChessboardBy updater arg1 arg2 gameplay =
        let chessboard = updater arg1 arg2 gameplay.Chessboard
        let gameplay = { gameplay with Chessboard = chessboard }
        Gameplay.syncLists gameplay
    
    static member relocateCharacter index coordinates gameplay =
        Gameplay.updateChessboardBy Chessboard.placeCharacter index coordinates gameplay
    
    static member addMove index (move : Move) gameplay =
        Gameplay.updateChessboardBy Chessboard.addMove index move gameplay

    static member removeMove index gameplay =
        Gameplay.updateChessboardBy Chessboard.removeMove index () gameplay
    
    static member truncatePlayerPath gameplay =
        Gameplay.updateChessboardBy Chessboard.truncatePlayerPath () () gameplay
    
    static member addHealth coordinates gameplay =
        Gameplay.updateChessboardBy Chessboard.updateCoordinatesValue (Some Health) coordinates gameplay

    static member removeHealth coordinates gameplay =
        Gameplay.updateChessboardBy Chessboard.updateCoordinatesValue None coordinates gameplay
    
    static member clearPickups gameplay =
        Gameplay.updateChessboardBy Chessboard.clearPickups () () gameplay
    
    static member removeEnemy index gameplay =
        let coordinates = Gameplay.getCoordinates index gameplay
        let gameplay = Gameplay.addHealth coordinates gameplay
        Gameplay.updateChessboardBy Chessboard.removeCharacter index () gameplay

    static member clearEnemies gameplay =
        Gameplay.updateChessboardBy Chessboard.clearEnemies () () gameplay

    static member finishMove index gameplay =
        let gameplay = Gameplay.removeMove index gameplay
        let gameplay = Gameplay.updateCharacterActivityState index (constant NoActivity) gameplay
        Gameplay.updateTurnStatus index (constant Idle) gameplay
    
    static member tryPickupHealth index coordinates gameplay =
        match index with
        | PlayerIndex ->
            let gameplay = Gameplay.updateCharacterState index (constant { gameplay.Player.CharacterState with HitPoints = 30 }) gameplay
            Gameplay.removeHealth coordinates gameplay
        | _ -> gameplay
    
    static member applyStep index direction gameplay =
        let coordinates = (Gameplay.getCoordinates index gameplay) + dtovm direction
        let gameplay =
            if Chessboard.pickupAtCoordinates coordinates gameplay.Chessboard then
                Gameplay.tryPickupHealth index coordinates gameplay
            else gameplay
        Gameplay.relocateCharacter index coordinates gameplay
    
    static member applyAttack reactorIndex gameplay =
        let reactorDamage = 4 // NOTE: just hard-coding damage for now
        let reactorState = Gameplay.getCharacterState reactorIndex gameplay
        Gameplay.updateCharacterState reactorIndex (constant { reactorState with HitPoints = reactorState.HitPoints - reactorDamage }) gameplay
    
    static member stopTravelingPlayer reactorIndex gameplay =
        if reactorIndex = PlayerIndex then Gameplay.truncatePlayerPath gameplay else gameplay
    
    static member applyMove index gameplay =
        let move = Gameplay.getCurrentMove index gameplay
        match move with
        | Step direction -> Gameplay.applyStep index direction gameplay
        | Attack reactorIndex ->
            let gameplay = Gameplay.applyAttack reactorIndex gameplay
            Gameplay.stopTravelingPlayer reactorIndex gameplay
        | Travel path ->
            match path with
            | head :: _ ->
                let currentCoordinates = Gameplay.getCoordinates index gameplay
                let direction = Math.directionToTarget currentCoordinates head.PositionM
                Gameplay.applyStep index direction gameplay
            | [] -> failwithumf ()
    
    static member activateCharacter index gameplay =
        let move = Gameplay.getCurrentMove index gameplay
        let activity = Gameplay.getPosition index gameplay |> vftovm |> move.MakeActivity
        let gameplay = Gameplay.updateCharacterActivityState index (constant activity) gameplay
        Gameplay.updateTurnStatus index (constant TurnBeginning) gameplay
    
    static member setFieldMap fieldMap gameplay =
        let gameplay = Gameplay.updateChessboardBy Chessboard.setPassableCoordinates () fieldMap gameplay
        let field = { FieldMapNp = fieldMap }
        Gameplay.updateField (constant field) gameplay

    static member transitionMap direction gameplay =
        let mapModeler = gameplay.MapModeler.Transition direction
        Gameplay.updateMapModeler (constant mapModeler) gameplay

    static member setCharacterPositionToCoordinates index gameplay =
        let position = Gameplay.getCoordinates index gameplay |> vmtovf
        Gameplay.updatePosition index (constant position) gameplay
    
    static member yankPlayer coordinates gameplay =
        let gameplay = Gameplay.relocateCharacter PlayerIndex coordinates gameplay
        Gameplay.setCharacterPositionToCoordinates PlayerIndex gameplay
    
    static member makePlayer gameplay =
        let gameplay = Gameplay.updateChessboardBy Chessboard.placeCharacter PlayerIndex v2iZero gameplay
        Gameplay.createPlayer gameplay

    static member makeEnemy index gameplay =
        let availableCoordinates = gameplay.Chessboard.AvailableCoordinates
        let coordinates = availableCoordinates.Item(Gen.random1 availableCoordinates.Length)
        Gameplay.updateChessboardBy Chessboard.placeCharacter index coordinates gameplay

    static member makeEnemies quantity gameplay =
        let rec recursion count gameplay =
            if count = quantity then gameplay
            else Gameplay.makeEnemy (EnemyIndex count) gameplay |> recursion (count + 1)
        recursion 0 gameplay
    
    static member forEachIndex updater indices gameplay =
        let rec recursion (indices : CharacterIndex list) gameplay =
            if indices.Length = 0 then gameplay
            else
                let index = indices.Head
                let characterOpt = Gameplay.tryGetCharacterByIndex index gameplay
                let gameplay =
                    match characterOpt with
                    | None -> gameplay
                    | Some _ -> updater index gameplay
                recursion indices.Tail gameplay
        recursion indices gameplay