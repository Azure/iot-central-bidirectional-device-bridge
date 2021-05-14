{
    data: .obj
        | map( { (.name | tostring): .value } )
        | add
}