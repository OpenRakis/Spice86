/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */


using System.Numerics;

namespace Spice86.Core.Backend.Audio.Iir;

/**
 * 
 * A complex pair
 *
 */
public class ComplexPair {
    public Complex First { get; }
    public Complex Second { get; }

    public ComplexPair(Complex c1, Complex c2) {
        First = c1;
        Second = c2;
    }

    public ComplexPair(Complex c1) {
        First = c1;
        Second = new Complex(0, 0);
    }

    public bool IsConjugate() {
        return Second.Equals(Complex.Conjugate(First));
    }

    public bool IsReal() {
        return First.Imaginary == 0 && Second.Imaginary == 0;
    }

    // Returns true if this is either a conjugate pair,
    // or a pair of reals where neither is zero.
    public bool IsMatchedPair() {
        if (First.Imaginary != 0) {
            return Second.Equals(Complex.Conjugate(First));
        } else {
            return Second.Imaginary == 0 &&
                Second.Real != 0 &&
                First.Real != 0;
        }
    }

    public bool IsNaN() {
        return Complex.IsNaN(First) || Complex.IsNaN(Second);
    }
};
