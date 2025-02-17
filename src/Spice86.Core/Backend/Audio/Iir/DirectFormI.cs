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

namespace Spice86.Core.Backend.Audio.Iir;

/**
 *
 * Implementation of a Direct Form I filter with its states. The coefficients
 * are supplied from the outside.
 *
 */
public class DirectFormI : DirectFormAbstract {
    public DirectFormI() {
        Reset();
    }

    public override void Reset() {
        _x1 = 0;
        _x2 = 0;
        _y1 = 0;
        _y2 = 0;
    }

    public override double Process1(double x, Biquad s) {
        double res = s.B0 * x + s.B1 * _x1 + s.B2 * _x2
                - s.A1 * _y1 - s.A2 * _y2;
        _x2 = _x1;
        _y2 = _y1;
        _x1 = x;
        _y1 = res;

        return res;
    }

    double _x2; // x[n-2]
    double _y2; // y[n-2]
    double _x1; // x[n-1]
    double _y1; // y[n-1]
};
